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

Imports Mono.Nat

Public Class UPnPHelper
    Public Shared Async Function OpenPorts(ParamArray ports() As Integer) As Task
        ' [DISABLED] UDP Hole Punching is used instead of UPnP for outbound connections
        Await Task.CompletedTask
    End Function

    Public Shared Async Function ClosePorts(ParamArray ports() As Integer) As Task
        ' 起動時に以前のセッションで開いたポートを閉じる
        Try
            Dim tcs As New TaskCompletionSource(Of INatDevice)()

            AddHandler NatUtility.DeviceFound, Sub(sender, args)
                                                   tcs.TrySetResult(args.Device)
                                               End Sub

            NatUtility.StartDiscovery()

            Dim cts As New Threading.CancellationTokenSource(5000)
            cts.Token.Register(Sub() tcs.TrySetCanceled())

            Dim device = Await tcs.Task
            NatUtility.StopDiscovery()

            For Each port In ports
                Try
                    Await device.DeletePortMapAsync(New Mapping(Protocol.Udp, port, port))
                    Debug.WriteLine($"[UPnP] Port {port} closed")
                Catch ex As Exception
                    Debug.WriteLine($"[UPnP] Port {port} close skipped: {ex.Message}")
                End Try
            Next
        Catch ex As Exception
            Debug.WriteLine($"[UPnP] ClosePorts failed: {ex.Message}")
        End Try
    End Function
End Class
