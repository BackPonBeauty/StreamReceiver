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
Imports SharpDX.XInput
Imports State = SharpDX.XInput.State

Public Class XInputSender

    Public Event ControllerDisconnected()

    Private _controller As Controller
    Private _udpClient As UdpClient
    Private _endPoint As IPEndPoint
    Private _running As Boolean
    Private _thread As Thread

    Public Sub Start(destIP As String, destPort As Integer, playerIndex As UserIndex)
        _controller = New Controller(playerIndex)
        _udpClient = New UdpClient()
        _endPoint = New IPEndPoint(IPAddress.Parse(destIP), destPort)
        _running = True
        _thread = New Thread(AddressOf SendLoop)
        _thread.IsBackground = True
        _thread.Start()
    End Sub

    Private Sub SendLoop()
        While _running
            If _controller.IsConnected Then
                Dim state As State
                _controller.GetState(state)
                Dim pkt = PackState(state.Gamepad)
                _udpClient.Send(pkt, pkt.Length, _endPoint)
            End If

            Thread.Sleep(16)
        End While
    End Sub

    Private Function PackState(g As Gamepad) As Byte()
        Dim buf(19) As Byte
        Dim buttons As UShort = CUShort(g.Buttons)
        buf(0) = CByte(buttons And &HFF)
        buf(1) = CByte((buttons >> 8) And &HFF)
        buf(2) = g.LeftTrigger
        buf(3) = g.RightTrigger
        BitConverter.GetBytes(g.LeftThumbX).CopyTo(buf, 4)
        BitConverter.GetBytes(g.LeftThumbY).CopyTo(buf, 6)
        BitConverter.GetBytes(g.RightThumbX).CopyTo(buf, 8)
        BitConverter.GetBytes(g.RightThumbY).CopyTo(buf, 10)
        Return buf
    End Function

    Public Sub [Stop]()
        _running = False
        _udpClient?.Close()
    End Sub

End Class