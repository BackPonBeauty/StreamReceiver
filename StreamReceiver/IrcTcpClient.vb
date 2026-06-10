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

Imports System.Net.Sockets
Imports System.IO
Imports System.Threading
Imports System.Text

Public Class IrcTcpClient

    Public Event MessageReceived(nick As String, message As String)
    Public Event UserJoined(nick As String)
    Public Event UserLeft(nick As String)
    Public Event Connected()
    Public Event Disconnected()
    Public Event ErrorOccurred(message As String)

    Private _tcp As TcpClient
    Private _writer As StreamWriter
    Private _thread As Thread
    Private _running As Boolean = False
    Private _joined As Boolean = False      ' JOIN確定フラグ
    Private _nickError As Boolean = False   ' nick errorフラグ

    Private ReadOnly _server As String
    Private ReadOnly _port As Integer
    Private ReadOnly _nick As String
    Private ReadOnly _channel As String

    Public Sub New(server As String, port As Integer, nick As String, channel As String)
        _server = server
        _port = port
        _nick = nick
        _channel = channel
    End Sub

    Public Sub Connect()
        _running = True
        _joined = False
        _nickError = False
        _thread = New Thread(AddressOf ReceiveLoop)
        _thread.IsBackground = True
        _thread.Start()
    End Sub

    Private Sub ReceiveLoop()
        Try
            _tcp = New TcpClient()
            _tcp.Connect(_server, _port)

            Dim stream = _tcp.GetStream()
            _writer = New StreamWriter(stream, New UTF8Encoding(False)) With {.AutoFlush = True}
            Dim reader As New StreamReader(stream, Encoding.UTF8)

            _writer.WriteLine($"NICK {_nick}")
            _writer.WriteLine($"USER {_nick} 0 * :{_nick}")

            Do While _running
                Dim line = reader.ReadLine()
                If line Is Nothing Then Exit Do
                ParseLine(line)
            Loop
        Catch ex As Exception
            If _running AndAlso Not _nickError Then
                RaiseEvent ErrorOccurred(ex.Message)
            End If
        Finally
            _running = False
            ' nick errorの場合はDisconnectedを発火しない
            If Not _nickError Then
                RaiseEvent Disconnected()
            End If
        End Try
    End Sub

    Private Sub ParseLine(line As String)
        Debug.WriteLine("[IRC] " & line)

        ' PING
        If line.StartsWith("PING") Then
            _writer.WriteLine(line.Replace("PING", "PONG"))
            Return
        End If

        ' 001 = 登録完了 → JOIN
        If line.Contains(" 001 ") Then
            _writer.WriteLine($"JOIN #{_channel}")
            Return
        End If

        ' 366 = NAMES list終了 = JOIN確定
        If line.Contains(" 366 ") Then
            _joined = True
            RaiseEvent Connected()
            Return
        End If

        ' 433 = ニックネーム使用中
        If line.Contains(" 433 ") Then
            _nickError = True
            _running = False
            RaiseEvent ErrorOccurred("Nickname already in use.")
            Try : _tcp?.Close() : Catch : End Try
            Return
        End If

        ' チャットメッセージ
        If line.Contains(" PRIVMSG ") Then
            Dim nick = ParseNick(line)
            Dim idx = line.IndexOf(" PRIVMSG ")
            Dim msg = line.Substring(idx)
            Dim colonIdx = msg.IndexOf(":", 1)
            If colonIdx >= 0 Then
                RaiseEvent MessageReceived(nick, msg.Substring(colonIdx + 1))
            End If
            Return
        End If

        ' JOIN
        If line.Contains(" JOIN ") Then
            Dim nick = ParseNick(line)
            If nick <> "" AndAlso nick <> _nick Then
                RaiseEvent UserJoined(nick)
            End If
            Return
        End If

        ' PART / QUIT
        If line.Contains(" PART ") OrElse line.Contains(" QUIT ") Then
            Dim nick = ParseNick(line)
            If nick <> "" AndAlso nick <> _nick Then
                RaiseEvent UserLeft(nick)
            End If
            Return
        End If
    End Sub

    Private Function ParseNick(line As String) As String
        If Not line.StartsWith(":") Then Return ""
        Dim excl = line.IndexOf("!")
        If excl < 0 Then Return ""
        Return line.Substring(1, excl - 1)
    End Function

    Public Sub SendMessage(message As String)
        Try
            _writer?.WriteLine($"PRIVMSG #{_channel} :{message}")
        Catch ex As Exception
            RaiseEvent ErrorOccurred(ex.Message)
        End Try
    End Sub

    Public Sub Disconnect()
        _running = False
        Try : _writer?.WriteLine("QUIT :bye") : Catch : End Try
        Try : _tcp?.Close() : Catch : End Try
    End Sub

End Class