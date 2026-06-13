' Copyright (C) 2024 BackPonBeauty
'
' This program is free software: you can redistribute it and/or modify
' it under the terms of the GNU General Public License as published by
' the Free Software Foundation, either version 3 of the License, or
' (at your option) any later version.
'
' This program is distributed in the hope that it will be useful,
' but WITHOUT ANY WARRANTY; without even the implied warranty of
' MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
' GNU General Public License for more details.
'
' You should have received a copy of the GNU General Public License
' along with this program. If not, see <https://www.gnu.org/licenses/>.

Imports System.Net
Imports System.Net.Sockets
Imports System.Threading
Imports System.Diagnostics

Public Class VideoReceiver
    Private _udpClient As UdpClient
    Private _ffmpeg As Process
    Private _rtpThread As Thread
    Private _readThread As Thread
    Private _running As Boolean = False

    Public ReadOnly Width As Integer
    Public ReadOnly Height As Integer
    Public ReadOnly FrameSize As Integer

    Private _bufA() As Byte
    Private _bufB() As Byte
    Private _frontIsA As Boolean = True
    Private _lock As New Object()
    
    Private _totalExpectedPackets As Long = 0
    Private _totalLostPackets As Long = 0
    Private _lossLock As New Object()

    ' RTPバッファを使い回す（毎パケットnewしない）
    Private _rtpBuf(65535) As Byte
    Private _fuBuffer As New System.IO.MemoryStream(65536)
    Private _fuStarted As Boolean = False
    Private _waitingForKeyframe As Boolean = True

    Public Event ServerDisconnected()

    Private _codec As String = "H265"

    Public Sub New(w As Integer, h As Integer, udpClient As UdpClient, Optional codec As String = "H265")
        Width = w
        Height = h
        FrameSize = w * h * 4
        ReDim _bufA(FrameSize - 1)
        ReDim _bufB(FrameSize - 1)
        _udpClient = udpClient
        _codec = codec
    End Sub

    Public Sub Start()
        _running = True

        Dim codecArg As String = If(_codec.ToUpper() = "H264", "h264", "hevc")
        _ffmpeg = New Process()
        With _ffmpeg.StartInfo
            .FileName = "ffmpeg.exe"
            .Arguments = $"-fflags nobuffer -flags low_delay -f {codecArg} -i pipe:0 -vf ""vflip,scale={Width}:{Height}"" -f rawvideo -pix_fmt bgra pipe:1"
            .UseShellExecute = False
            .RedirectStandardInput = True
            .RedirectStandardOutput = True
            .RedirectStandardError = True
            .CreateNoWindow = True
        End With
        _ffmpeg.Start()
        _ffmpeg.BeginErrorReadLine()

        _rtpThread = New Thread(AddressOf RtpReceiveLoop)
        _rtpThread.IsBackground = True
        _rtpThread.Priority = ThreadPriority.AboveNormal
        _rtpThread.Start()

        _readThread = New Thread(AddressOf FrameReadLoop)
        _readThread.IsBackground = True
        _readThread.Priority = ThreadPriority.AboveNormal
        _readThread.Start()
    End Sub

    Private Sub RtpReceiveLoop()
        Dim ep As New IPEndPoint(IPAddress.Any, 0)
        Dim lastReceived As DateTime = DateTime.Now
        ' _udpClient.Client を直接使う（UdpClient.Receiveより速い）
        Dim sock = _udpClient.Client
        sock.ReceiveTimeout = 10000

        Dim isH264 As Boolean = (_codec.ToUpper() = "H264")
        Dim lastSeq As Integer = -1

        Do While _running
            Try
                ' ゼロアロケーション受信
                Dim received As Integer = sock.ReceiveFrom(_rtpBuf, SocketFlags.None, ep)
                lastReceived = DateTime.Now
                If received < 13 Then Continue Do

                Dim seq As UShort = (CUShort(_rtpBuf(2)) << 8) Or _rtpBuf(3)
                SyncLock _lossLock
                    If lastSeq = -1 Then
                        lastSeq = seq
                        _totalExpectedPackets += 1
                    Else
                        Dim diff As Integer = CInt(seq) - lastSeq
                        If diff < 0 Then
                            diff += 65536
                        End If

                        If diff > 0 AndAlso diff < 3000 Then
                            _totalExpectedPackets += diff
                            _totalLostPackets += (diff - 1)
                            lastSeq = seq
                        ElseIf diff >= 3000 Then
                            ' Reset tracking
                            lastSeq = seq
                            _totalExpectedPackets += 1
                        End If
                    End If
                End SyncLock

                If Not isH264 Then
                    ' HEVC NAL Type is 6 bits from byte 12
                    Dim nalType = (_rtpBuf(12) And &H7E) >> 1

                    If nalType >= 0 AndAlso nalType <= 31 Then
                        If nalType = 32 OrElse nalType = 33 OrElse nalType = 34 Then _waitingForKeyframe = False
                        If Not _waitingForKeyframe Then
                            WriteNalToFFmpeg(_rtpBuf, 12, received - 12)
                        End If

                    ElseIf nalType = 49 Then
                        Dim fuHeader = _rtpBuf(14)
                        Dim startBit = (fuHeader And &H80) <> 0
                        Dim endBit = (fuHeader And &H40) <> 0
                        Dim fuNalType = fuHeader And &H3F

                        If startBit Then
                            If fuNalType = 32 OrElse fuNalType = 33 OrElse fuNalType = 34 OrElse fuNalType = 19 OrElse fuNalType = 20 Then
                                _waitingForKeyframe = False
                            End If
                            _fuBuffer.SetLength(0)

                            ' Reconstruct 2-byte HEVC NAL Header
                            Dim nalHeader0 = CByte((fuNalType << 1) Or (_rtpBuf(12) And &H1))
                            Dim nalHeader1 = _rtpBuf(13)
                            _fuBuffer.WriteByte(nalHeader0)
                            _fuBuffer.WriteByte(nalHeader1)
                            _fuStarted = True
                        End If

                        If _fuStarted Then
                            ' payload data starts at RTP offset 15
                            _fuBuffer.Write(_rtpBuf, 15, received - 15)
                            If endBit Then
                                If Not _waitingForKeyframe Then
                                    Dim nal = _fuBuffer.GetBuffer()
                                    WriteNalToFFmpeg(nal, 0, CInt(_fuBuffer.Length))
                                End If
                                _fuStarted = False
                            End If
                        End If
                    End If
                Else
                    ' H264 NAL Type is 5 bits from byte 12
                    Dim nalType = _rtpBuf(12) And &H1F

                    If nalType >= 1 AndAlso nalType <= 23 Then
                        If nalType = 7 OrElse nalType = 8 OrElse nalType = 5 Then _waitingForKeyframe = False
                        If Not _waitingForKeyframe Then
                            WriteNalToFFmpeg(_rtpBuf, 12, received - 12)
                        End If

                    ElseIf nalType = 28 Then ' H264 FU-A
                        Dim fuHeader = _rtpBuf(13)
                        Dim startBit = (fuHeader And &H80) <> 0
                        Dim endBit = (fuHeader And &H40) <> 0
                        Dim fuNalType = fuHeader And &H1F

                        If startBit Then
                            If fuNalType = 7 OrElse fuNalType = 8 OrElse fuNalType = 5 Then
                                _waitingForKeyframe = False
                            End If
                            _fuBuffer.SetLength(0)

                            ' Reconstruct 1-byte H.264 NAL Header
                            Dim nalHeader = CByte((_rtpBuf(12) And &H60) Or fuNalType)
                            _fuBuffer.WriteByte(nalHeader)
                            _fuStarted = True
                        End If

                        If _fuStarted Then
                            ' payload data starts at RTP offset 14
                            _fuBuffer.Write(_rtpBuf, 14, received - 14)
                            If endBit Then
                                If Not _waitingForKeyframe Then
                                    Dim nal = _fuBuffer.GetBuffer()
                                    WriteNalToFFmpeg(nal, 0, CInt(_fuBuffer.Length))
                                End If
                                _fuStarted = False
                            End If
                        End If
                    End If
                End If
            Catch ex As SocketException
                If (DateTime.Now - lastReceived).TotalSeconds > 50 Then
                    RaiseEvent ServerDisconnected()
                    Return
                End If
            Catch ex As Exception
                If _running Then Debug.WriteLine("[RTP] Error: " & ex.Message)
            End Try
        Loop
    End Sub

    Private ReadOnly _startCode As Byte() = {0, 0, 0, 1}

    ' オフセット付きオーバーロード（コピーなしで書き込む）
    Private Sub WriteNalToFFmpeg(buf() As Byte, offset As Integer, length As Integer)
        Try
            Dim stream = _ffmpeg.StandardInput.BaseStream
            stream.Write(_startCode, 0, 4)
            stream.Write(buf, offset, length)
            stream.Flush()
        Catch
        End Try
    End Sub

    Private Sub FrameReadLoop()
        Dim stream = _ffmpeg.StandardOutput.BaseStream
        Dim zeroCount As Integer = 0

        Do While _running
            Dim back() As Byte
            SyncLock _lock
                back = If(_frontIsA, _bufB, _bufA)
            End SyncLock

            Dim offset As Integer = 0
            Do While offset < FrameSize
                Dim read = stream.Read(back, offset, FrameSize - offset)
                If read = 0 Then
                    zeroCount += 1
                    ' 一時的な詰まりは無視、3回連続でようやく終了判定
                    If zeroCount >= 3 Then
                        _running = False
                        Return
                    End If
                    Thread.Sleep(1)
                Else
                    zeroCount = 0
                    offset += read
                End If
            Loop

            SyncLock _lock
                _frontIsA = Not _frontIsA
            End SyncLock
        Loop
    End Sub

    Public Function TryGetFrame(dst() As Byte) As Boolean
        SyncLock _lock
            Dim front() As Byte = If(_frontIsA, _bufA, _bufB)
            Buffer.BlockCopy(front, 0, dst, 0, FrameSize)
            Return True
        End SyncLock
    End Function

    Public Function GetAndResetLossRate() As Double
        SyncLock _lossLock
            If _totalExpectedPackets = 0 Then
                Return 0.0
            End If
            Dim rate As Double = CDbl(_totalLostPackets) / CDbl(_totalExpectedPackets)
            _totalExpectedPackets = 0
            _totalLostPackets = 0
            Return rate
        End SyncLock
    End Function

    Public Sub [Stop]()
        _running = False
        Try : _udpClient?.Close() : Catch : End Try
        Try : _ffmpeg?.Kill() : Catch : End Try
    End Sub

    Public Sub SendDummy(ip As String)
        Try
            Dim ep As New IPEndPoint(IPAddress.Parse(ip), 5002)
            _udpClient.Send(New Byte() {0}, 1, ep)
            Debug.WriteLine("[NAT] Dummy sent to " & ip & ":5002")
        Catch ex As Exception
            Debug.WriteLine("[NAT] SendDummy error: " & ex.Message)
        End Try
    End Sub
End Class