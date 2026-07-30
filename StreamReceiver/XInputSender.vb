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
Imports System.Windows.Forms
Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports SharpDX.DirectInput

Public Class XInputSender

    <DllImport("winmm.dll")>
    Private Shared Function timeBeginPeriod(uPeriod As UInteger) As Integer
    End Function
    <DllImport("winmm.dll")>
    Private Shared Function timeEndPeriod(uPeriod As UInteger) As Integer
    End Function

    Public Event ControllerDisconnected()

    Private _controller As Controller
    Private _directInput As DirectInput
    Private _dinputJoystick As Joystick

    Private _udpClient As UdpClient
    Private _endPoint As IPEndPoint
    Private _running As Boolean
    Private _thread As Thread

    ' Keyboard fallback state
    Private Shared _pressedKeys As New HashSet(Of Keys)()
    Private Shared _keysLock As New Object()

    ' Meta-key flags sent to host
    Public Const METAKEY_ALT_D        As UShort = 1        ' bit 0
    Public Const METAKEY_BITRATE_UP   As UShort = 2        ' bit 1
    Public Const METAKEY_BITRATE_DOWN As UShort = 4        ' bit 2
    Private Shared _metaKeys As UShort = 0
    Private Shared _metaLock As New Object()

    Public Shared Sub SetMetaKey(bit As UShort, pressed As Boolean)
        SyncLock _metaLock
            If pressed Then
                _metaKeys = _metaKeys Or bit
            Else
                _metaKeys = _metaKeys And Not bit
            End If
        End SyncLock
    End Sub

    Public Shared KeyMapping As New Dictionary(Of String, Keys) From {
        {"DpadUp", Keys.None},
        {"DpadDown", Keys.None},
        {"DpadLeft", Keys.None},
        {"DpadRight", Keys.None},
        {"A", Keys.J},
        {"B", Keys.K},
        {"X", Keys.U},
        {"Y", Keys.I},
        {"LB", Keys.None},
        {"RB", Keys.None},
        {"LTrigger", Keys.None},
        {"RTrigger", Keys.None},
        {"LThumb", Keys.None},
        {"RThumb", Keys.None},
        {"Start", Keys.Enter},
        {"Back", Keys.Space},
        {"LStickUp", Keys.W},
        {"LStickDown", Keys.S},
        {"LStickLeft", Keys.A},
        {"LStickRight", Keys.D},
        {"RStickUp", Keys.None},
        {"RStickDown", Keys.None},
        {"RStickLeft", Keys.None},
        {"RStickRight", Keys.None}
    }

    ' Controller input mapping (Action -> Physical Input Name)
    Public Shared PadMapping As New Dictionary(Of String, String) From {
        {"DpadUp", "DpadUp"},
        {"DpadDown", "DpadDown"},
        {"DpadLeft", "DpadLeft"},
        {"DpadRight", "DpadRight"},
        {"A", "A"},
        {"B", "B"},
        {"X", "X"},
        {"Y", "Y"},
        {"LB", "LB"},
        {"RB", "RB"},
        {"LTrigger", "LTrigger"},
        {"RTrigger", "RTrigger"},
        {"LThumb", "LThumb"},
        {"RThumb", "RThumb"},
        {"Start", "Start"},
        {"Back", "Back"},
        {"LStickUp", "LStickUp"},
        {"LStickDown", "LStickDown"},
        {"LStickLeft", "LStickLeft"},
        {"LStickRight", "LStickRight"},
        {"RStickUp", "RStickUp"},
        {"RStickDown", "RStickDown"},
        {"RStickLeft", "RStickLeft"},
        {"RStickRight", "RStickRight"}
    }

    Public Shared Sub UpdateKeyState(key As Keys, isPressed As Boolean)
        SyncLock _keysLock
            If isPressed Then
                _pressedKeys.Add(key)
            Else
                _pressedKeys.Remove(key)
            End If
        End SyncLock
    End Sub

    Public Shared Sub LoadConfig()
        Dim path = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.cfg")
        If Not IO.File.Exists(path) Then
            SaveConfig()
            Return
        End If

        Try
            ' Pre-populate defaults in case config is partial
            Dim lines = IO.File.ReadAllLines(path)
            For Each line In lines
                Dim parts = line.Split("="c)
                If parts.Length = 2 Then
                    Dim keyName = parts(0).Trim()
                    Dim val = parts(1).Trim()

                    If keyName.StartsWith("Kb_") Then
                        Dim action = keyName.Substring(3)
                        Dim keyVal As Keys
                        If [Enum].TryParse(Of Keys)(val, True, keyVal) Then
                            If KeyMapping.ContainsKey(action) Then
                                KeyMapping(action) = keyVal
                            End If
                        End If
                    ElseIf keyName.StartsWith("Pad_") Then
                        Dim action = keyName.Substring(4)
                        If PadMapping.ContainsKey(action) Then
                            PadMapping(action) = val
                        End If
                    End If
                End If
            Next
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine("[Config] Load error: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub SaveConfig()
        Dim path = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.cfg")
        Try
            Dim lines As New List(Of String)()
            For Each kvp In KeyMapping
                lines.Add($"Kb_{kvp.Key}={kvp.Value.ToString()}")
            Next
            For Each kvp In PadMapping
                lines.Add($"Pad_{kvp.Key}={kvp.Value}")
            Next
            IO.File.WriteAllLines(path, lines.ToArray())
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine("[Config] Save error: " & ex.Message)
        End Try
    End Sub

    Public Sub Start(destIP As String, destPort As Integer, playerIndex As UserIndex)
        _controller = New Controller(playerIndex)
        InitDirectInput()

        _udpClient = New UdpClient()
        _endPoint = New IPEndPoint(IPAddress.Parse(destIP), destPort)
        _running = True
        _thread = New Thread(AddressOf SendLoop)
        _thread.IsBackground = True
        _thread.Start()
    End Sub

    Private _dinputJoysticks As New List(Of Joystick)()

    Private Sub InitDirectInput()
        Try
            If _directInput Is Nothing Then
                _directInput = New DirectInput()
            End If

            ' Clear existing joysticks
            For Each j In _dinputJoysticks
                Try
                    j.Unacquire()
                    j.Dispose()
                Catch
                End Try
            Next
            _dinputJoysticks.Clear()

            Dim allDevices As New List(Of DeviceInstance)()
            allDevices.AddRange(_directInput.GetDevices(SharpDX.DirectInput.DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly))
            allDevices.AddRange(_directInput.GetDevices(SharpDX.DirectInput.DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly))

            If allDevices.Count = 0 Then
                allDevices.AddRange(_directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
            End If

            System.Diagnostics.Debug.WriteLine($"[DInput] Found {allDevices.Count} DirectInput devices.")

            For i = 0 To allDevices.Count - 1
                Try
                    Dim joy = New Joystick(_directInput, allDevices(i).InstanceGuid)
                    joy.SetCooperativeLevel(IntPtr.Zero, CooperativeLevel.Background Or CooperativeLevel.NonExclusive)
                    
                    For Each deviceObject In joy.GetObjects(DeviceObjectTypeFlags.Axis)
                        joy.GetObjectPropertiesById(deviceObject.ObjectId).Range = New InputRange(0, 65535)
                    Next

                    joy.Acquire()
                    _dinputJoysticks.Add(joy)
                    System.Diagnostics.Debug.WriteLine($"[DInput] Initialized DirectInput device #{i+1}: {joy.Information.InstanceName}")
                Catch ex As Exception
                    System.Diagnostics.Debug.WriteLine($"[DInput] Failed to init device #{i+1}: {ex.Message}")
                End Try
            Next
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine("[DInput] Init error: " & ex.Message)
        End Try
    End Sub

    Private Sub SendLoop()
        timeBeginPeriod(1)
        Try
            While _running
                Dim pkt() As Byte = Nothing

                ' 1. Check XInput Controller (Only accept if there is ACTIVE user input)
                If _controller IsNot Nothing AndAlso _controller.IsConnected Then
                    Try
                        Dim state As State
                        _controller.GetState(state)
                        Dim xPkt = GetMappedGamepadPacket(state.Gamepad)
                        If IsXInputPacketActive(xPkt) Then
                            pkt = xPkt
                        End If
                    Catch ex As Exception
                    End Try
                End If

                ' 2. Check DirectInput Controllers (e.g. Logicool Dual Action USB)
                If pkt Is Nothing Then
                    If _dinputJoysticks.Count = 0 Then
                        InitDirectInput()
                    End If

                    For Each joy In _dinputJoysticks
                        Try
                            joy.Poll()
                            Dim dState = joy.GetCurrentState()
                            Dim tempPkt = GetMappedDInputPacket(dState)
                            
                            If IsDInputPacketActive(tempPkt) Then
                                pkt = tempPkt
                                Exit For
                            ElseIf pkt Is Nothing Then
                                ' Keep neutral DInput packet as fallback if connected
                                pkt = tempPkt
                            End If
                        Catch ex As Exception
                            Try : joy.Acquire() : Catch : End Try
                        End Try
                    Next
                End If

                ' 3. Fallback to Keyboard
                If pkt Is Nothing Then
                    pkt = GetKeyboardStatePacket()
                End If

                _udpClient.Send(pkt, pkt.Length, _endPoint)

                Thread.Sleep(8)
            End While
        Finally
            timeEndPeriod(1)
        End Try
    End Sub


    ' Detect active controls on a controller (used for configuration screen)
    Public Shared Function ScanActiveControllerInput(g As Gamepad) As String
        ' Thresholds
        Const TriggerThreshold As Byte = 100
        Const StickThreshold As Short = 15000

        ' Check normal buttons
        Dim b = g.Buttons
        If (b And GamepadButtonFlags.A) <> 0 Then Return "A"
        If (b And GamepadButtonFlags.B) <> 0 Then Return "B"
        If (b And GamepadButtonFlags.X) <> 0 Then Return "X"
        If (b And GamepadButtonFlags.Y) <> 0 Then Return "Y"
        If (b And GamepadButtonFlags.Start) <> 0 Then Return "Start"
        If (b And GamepadButtonFlags.Back) <> 0 Then Return "Back"
        If (b And GamepadButtonFlags.LeftShoulder) <> 0 Then Return "LB"
        If (b And GamepadButtonFlags.RightShoulder) <> 0 Then Return "RB"
        If (b And GamepadButtonFlags.LeftThumb) <> 0 Then Return "LThumb"
        If (b And GamepadButtonFlags.RightThumb) <> 0 Then Return "RThumb"
        If (b And GamepadButtonFlags.DPadUp) <> 0 Then Return "DpadUp"
        If (b And GamepadButtonFlags.DPadDown) <> 0 Then Return "DpadDown"
        If (b And GamepadButtonFlags.DPadLeft) <> 0 Then Return "DpadLeft"
        If (b And GamepadButtonFlags.DPadRight) <> 0 Then Return "DpadRight"

        ' Check triggers
        If g.LeftTrigger > TriggerThreshold Then Return "LTrigger"
        If g.RightTrigger > TriggerThreshold Then Return "RTrigger"

        ' Check sticks
        If g.LeftThumbY > StickThreshold Then Return "LStickUp"
        If g.LeftThumbY < -StickThreshold Then Return "LStickDown"
        If g.LeftThumbX < -StickThreshold Then Return "LStickLeft"
        If g.LeftThumbX > StickThreshold Then Return "LStickRight"

        If g.RightThumbY > StickThreshold Then Return "RStickUp"
        If g.RightThumbY < -StickThreshold Then Return "RStickDown"
        If g.RightThumbX < -StickThreshold Then Return "RStickLeft"
        If g.RightThumbX > StickThreshold Then Return "RStickRight"

        Return Nothing
    End Function

    Public Shared Function ScanActiveDInputInput(state As JoystickState) As String
        Dim b = state.Buttons
        If b.Length > 0 AndAlso b(0) Then Return "X"        ' Button 1
        If b.Length > 1 AndAlso b(1) Then Return "A"        ' Button 2
        If b.Length > 2 AndAlso b(2) Then Return "B"        ' Button 3
        If b.Length > 3 AndAlso b(3) Then Return "Y"        ' Button 4
        If b.Length > 4 AndAlso b(4) Then Return "LB"       ' Button 5
        If b.Length > 5 AndAlso b(5) Then Return "RB"       ' Button 6
        If b.Length > 6 AndAlso b(6) Then Return "LTrigger" ' Button 7
        If b.Length > 7 AndAlso b(7) Then Return "RTrigger" ' Button 8
        If b.Length > 8 AndAlso b(8) Then Return "Back"     ' Button 9
        If b.Length > 9 AndAlso b(9) Then Return "Start"    ' Button 10
        If b.Length > 10 AndAlso b(10) Then Return "LThumb" ' Button 11
        If b.Length > 11 AndAlso b(11) Then Return "RThumb" ' Button 12

        Dim povs = state.PointOfViewControllers
        If povs IsNot Nothing AndAlso povs.Length > 0 Then
            Dim pov = povs(0)
            If pov >= 0 AndAlso pov < 36000 Then
                If pov >= 31500 OrElse pov <= 4500 Then Return "DpadUp"
                If pov >= 4500 AndAlso pov <= 13500 Then Return "DpadRight"
                If pov >= 13500 AndAlso pov <= 22500 Then Return "DpadDown"
                If pov >= 22500 AndAlso pov <= 31500 Then Return "DpadLeft"
            End If
        End If

        Const StickThreshold As Integer = 15000

        ' Left Stick (X, Y)
        If state.Y < 32768 - StickThreshold Then Return "LStickUp"
        If state.Y > 32768 + StickThreshold Then Return "LStickDown"
        If state.X < 32768 - StickThreshold Then Return "LStickLeft"
        If state.X > 32768 + StickThreshold Then Return "LStickRight"

        ' Right Stick (Logicool Dual Action: Z=RX, RotationZ/Y=RY)
        Dim rawZ = If(state.Z > 0, state.Z, state.RotationX)
        Dim rawRotY = If(state.RotationZ > 0, state.RotationZ, state.RotationY)

        If rawZ > 0 Then
            If rawZ < 32768 - StickThreshold Then Return "RStickLeft"
            If rawZ > 32768 + StickThreshold Then Return "RStickRight"
        End If

        If rawRotY > 0 Then
            If rawRotY < 32768 - StickThreshold Then Return "RStickUp"
            If rawRotY > 32768 + StickThreshold Then Return "RStickDown"
        End If

        Return Nothing
    End Function

    Private Function GetMappedGamepadPacket(g As Gamepad) As Byte()
        Dim buttons As UShort = 0
        Dim leftTrigger As Byte = 0
        Dim rightTrigger As Byte = 0
        Dim thumbLX As Short = 0
        Dim thumbLY As Short = 0
        Dim thumbRX As Short = 0
        Dim thumbRY As Short = 0

        If IsPadActionActive(g, "DpadUp") Then buttons = buttons Or &H1
        If IsPadActionActive(g, "DpadDown") Then buttons = buttons Or &H2
        If IsPadActionActive(g, "DpadLeft") Then buttons = buttons Or &H4
        If IsPadActionActive(g, "DpadRight") Then buttons = buttons Or &H8
        If IsPadActionActive(g, "Start") Then buttons = buttons Or &H10
        If IsPadActionActive(g, "Back") Then buttons = buttons Or &H20
        If IsPadActionActive(g, "LThumb") Then buttons = buttons Or &H40
        If IsPadActionActive(g, "RThumb") Then buttons = buttons Or &H80
        If IsPadActionActive(g, "LB") Then buttons = buttons Or &H100
        If IsPadActionActive(g, "RB") Then buttons = buttons Or &H200
        If IsPadActionActive(g, "A") Then buttons = buttons Or &H1000
        If IsPadActionActive(g, "B") Then buttons = buttons Or &H2000
        If IsPadActionActive(g, "X") Then buttons = buttons Or &H4000
        If IsPadActionActive(g, "Y") Then buttons = buttons Or &H8000

        ' Triggers: PadMappingがトリガー軸ならば実値(0-255)を送る
        Dim lTrigPhys = If(PadMapping.ContainsKey("LTrigger"), PadMapping("LTrigger"), "LTrigger")
        If lTrigPhys = "LTrigger" Then
            leftTrigger = g.LeftTrigger
        ElseIf IsPadActionActive(g, "LTrigger") Then
            leftTrigger = 255
        End If
        Dim rTrigPhys = If(PadMapping.ContainsKey("RTrigger"), PadMapping("RTrigger"), "RTrigger")
        If rTrigPhys = "RTrigger" Then
            rightTrigger = g.RightTrigger
        ElseIf IsPadActionActive(g, "RTrigger") Then
            rightTrigger = 255
        End If

        ' Left Stick: PadMappingがスティック軸ならば実アナログ値(デッドゾーン4000)を送る
        Const LStickDeadzone As Integer = 4000
        Dim lsLeftPhys = If(PadMapping.ContainsKey("LStickLeft"), PadMapping("LStickLeft"), "LStickLeft")
        Dim lsUpPhys = If(PadMapping.ContainsKey("LStickUp"), PadMapping("LStickUp"), "LStickUp")
        If lsLeftPhys.Contains("LStick") OrElse lsUpPhys.Contains("LStick") Then
            If Math.Abs(CInt(g.LeftThumbX)) > LStickDeadzone Then thumbLX = g.LeftThumbX
            If Math.Abs(CInt(g.LeftThumbY)) > LStickDeadzone Then thumbLY = g.LeftThumbY
        Else
            If IsPadActionActive(g, "LStickUp") Then thumbLY = 32767
            If IsPadActionActive(g, "LStickDown") Then thumbLY = -32768
            If IsPadActionActive(g, "LStickLeft") Then thumbLX = -32768
            If IsPadActionActive(g, "LStickRight") Then thumbLX = 32767
        End If

        ' Right Stick: 同様
        Const RStickDeadzone As Integer = 4000
        Dim rsLeftPhys = If(PadMapping.ContainsKey("RStickLeft"), PadMapping("RStickLeft"), "RStickLeft")
        Dim rsUpPhys = If(PadMapping.ContainsKey("RStickUp"), PadMapping("RStickUp"), "RStickUp")
        If rsLeftPhys.Contains("RStick") OrElse rsUpPhys.Contains("RStick") Then
            If Math.Abs(CInt(g.RightThumbX)) > RStickDeadzone Then thumbRX = g.RightThumbX
            If Math.Abs(CInt(g.RightThumbY)) > RStickDeadzone Then thumbRY = g.RightThumbY
        Else
            If IsPadActionActive(g, "RStickUp") Then thumbRY = 32767
            If IsPadActionActive(g, "RStickDown") Then thumbRY = -32768
            If IsPadActionActive(g, "RStickLeft") Then thumbRX = -32768
            If IsPadActionActive(g, "RStickRight") Then thumbRX = 32767
        End If

        Dim buf(19) As Byte
        buf(0) = CByte(buttons And &HFF)
        buf(1) = CByte((buttons >> 8) And &HFF)
        buf(2) = leftTrigger
        buf(3) = rightTrigger
        BitConverter.GetBytes(thumbLX).CopyTo(buf, 4)
        BitConverter.GetBytes(thumbLY).CopyTo(buf, 6)
        BitConverter.GetBytes(thumbRX).CopyTo(buf, 8)
        BitConverter.GetBytes(thumbRY).CopyTo(buf, 10)
        Dim mk As UShort
        SyncLock _metaLock : mk = _metaKeys : End SyncLock
        BitConverter.GetBytes(mk).CopyTo(buf, 12)
        Return buf
    End Function

    Private Function IsPadActionActive(g As Gamepad, action As String) As Boolean
        If Not PadMapping.ContainsKey(action) Then Return False
        Dim physicalInput = PadMapping(action)

        ' Check normal buttons
        Dim b = g.Buttons
        Select Case physicalInput
            Case "A" : Return (b And GamepadButtonFlags.A) <> 0
            Case "B" : Return (b And GamepadButtonFlags.B) <> 0
            Case "X" : Return (b And GamepadButtonFlags.X) <> 0
            Case "Y" : Return (b And GamepadButtonFlags.Y) <> 0
            Case "Start" : Return (b And GamepadButtonFlags.Start) <> 0
            Case "Back" : Return (b And GamepadButtonFlags.Back) <> 0
            Case "LB" : Return (b And GamepadButtonFlags.LeftShoulder) <> 0
            Case "RB" : Return (b And GamepadButtonFlags.RightShoulder) <> 0
            Case "LThumb" : Return (b And GamepadButtonFlags.LeftThumb) <> 0
            Case "RThumb" : Return (b And GamepadButtonFlags.RightThumb) <> 0
            Case "DpadUp" : Return (b And GamepadButtonFlags.DPadUp) <> 0
            Case "DpadDown" : Return (b And GamepadButtonFlags.DPadDown) <> 0
            Case "DpadLeft" : Return (b And GamepadButtonFlags.DPadLeft) <> 0
            Case "DpadRight" : Return (b And GamepadButtonFlags.DPadRight) <> 0
        End Select

        ' Check triggers (threshold 80)
        If physicalInput = "LTrigger" AndAlso g.LeftTrigger > 80 Then Return True
        If physicalInput = "RTrigger" AndAlso g.RightTrigger > 80 Then Return True

        ' Check sticks (threshold 12000)
        Select Case physicalInput
            Case "LStickUp" : Return g.LeftThumbY > 12000
            Case "LStickDown" : Return g.LeftThumbY < -12000
            Case "LStickLeft" : Return g.LeftThumbX < -12000
            Case "LStickRight" : Return g.LeftThumbX > 12000
            Case "RStickUp" : Return g.RightThumbY > 12000
            Case "RStickDown" : Return g.RightThumbY < -12000
            Case "RStickLeft" : Return g.RightThumbX < -12000
            Case "RStickRight" : Return g.RightThumbX > 12000
        End Select

        Return False
    End Function

    Private Function GetKeyboardStatePacket() As Byte()
        Dim buttons As UShort = 0
        Dim leftTrigger As Byte = 0
        Dim rightTrigger As Byte = 0
        Dim thumbLX As Short = 0
        Dim thumbLY As Short = 0
        Dim thumbRX As Short = 0
        Dim thumbRY As Short = 0

        SyncLock _keysLock
            If IsKeyPressed("DpadUp") Then buttons = buttons Or &H1
            If IsKeyPressed("DpadDown") Then buttons = buttons Or &H2
            If IsKeyPressed("DpadLeft") Then buttons = buttons Or &H4
            If IsKeyPressed("DpadRight") Then buttons = buttons Or &H8
            If IsKeyPressed("Start") Then buttons = buttons Or &H10
            If IsKeyPressed("Back") Then buttons = buttons Or &H20
            If IsKeyPressed("LThumb") Then buttons = buttons Or &H40
            If IsKeyPressed("RThumb") Then buttons = buttons Or &H80
            If IsKeyPressed("LB") Then buttons = buttons Or &H100
            If IsKeyPressed("RB") Then buttons = buttons Or &H200
            If IsKeyPressed("A") Then buttons = buttons Or &H1000
            If IsKeyPressed("B") Then buttons = buttons Or &H2000
            If IsKeyPressed("X") Then buttons = buttons Or &H4000
            If IsKeyPressed("Y") Then buttons = buttons Or &H8000

            If IsKeyPressed("LTrigger") Then leftTrigger = 255
            If IsKeyPressed("RTrigger") Then rightTrigger = 255

            If IsKeyPressed("LStickUp") Then thumbLY = 32767
            If IsKeyPressed("LStickDown") Then thumbLY = -32768
            If IsKeyPressed("LStickLeft") Then thumbLX = -32768
            If IsKeyPressed("LStickRight") Then thumbLX = 32767

            If IsKeyPressed("RStickUp") Then thumbRY = 32767
            If IsKeyPressed("RStickDown") Then thumbRY = -32768
            If IsKeyPressed("RStickLeft") Then thumbRX = -32768
            If IsKeyPressed("RStickRight") Then thumbRX = 32767
        End SyncLock

        Dim buf(19) As Byte
        buf(0) = CByte(buttons And &HFF)
        buf(1) = CByte((buttons >> 8) And &HFF)
        buf(2) = leftTrigger
        buf(3) = rightTrigger
        BitConverter.GetBytes(thumbLX).CopyTo(buf, 4)
        BitConverter.GetBytes(thumbLY).CopyTo(buf, 6)
        BitConverter.GetBytes(thumbRX).CopyTo(buf, 8)
        BitConverter.GetBytes(thumbRY).CopyTo(buf, 10)
        Dim mk As UShort
        SyncLock _metaLock : mk = _metaKeys : End SyncLock
        BitConverter.GetBytes(mk).CopyTo(buf, 12)
        Return buf
    End Function

    Private Function GetMappedDInputPacket(state As JoystickState) As Byte()
        Dim buttons As UShort = 0
        Dim leftTrigger As Byte = 0
        Dim rightTrigger As Byte = 0
        Dim thumbLX As Short = 0
        Dim thumbLY As Short = 0
        Dim thumbRX As Short = 0
        Dim thumbRY As Short = 0

        ' 1. Buttons (Face, Shoulder, DPad, Start, Back, Thumb clicks)
        If IsDInputActionActive(state, "DpadUp")    Then buttons = buttons Or &H1
        If IsDInputActionActive(state, "DpadDown")  Then buttons = buttons Or &H2
        If IsDInputActionActive(state, "DpadLeft")  Then buttons = buttons Or &H4
        If IsDInputActionActive(state, "DpadRight") Then buttons = buttons Or &H8
        If IsDInputActionActive(state, "Start") Then buttons = buttons Or &H10
        If IsDInputActionActive(state, "Back") Then buttons = buttons Or &H20
        If IsDInputActionActive(state, "LThumb") Then buttons = buttons Or &H40
        If IsDInputActionActive(state, "RThumb") Then buttons = buttons Or &H80
        If IsDInputActionActive(state, "LB") Then buttons = buttons Or &H100
        If IsDInputActionActive(state, "RB") Then buttons = buttons Or &H200
        If IsDInputActionActive(state, "A") Then buttons = buttons Or &H1000
        If IsDInputActionActive(state, "B") Then buttons = buttons Or &H2000
        If IsDInputActionActive(state, "X") Then buttons = buttons Or &H4000
        If IsDInputActionActive(state, "Y") Then buttons = buttons Or &H8000

        If IsDInputActionActive(state, "LTrigger") Then leftTrigger = 255
        If IsDInputActionActive(state, "RTrigger") Then rightTrigger = 255

        ' 2. Left Stick: X/Y軸をアナログ値として変換（DPadとは独立）
        ' DirectInput: 0-65535 (center 32768) → XInput: -32768 to 32767
        Dim rawX = CInt(state.X) - 32768
        Dim rawY = 32767 - CInt(state.Y)  ' Y軸反転
        Const AxisDeadzone As Integer = 4000

        If Math.Abs(rawX) > AxisDeadzone Then thumbLX = CShort(Math.Max(-32768, Math.Min(32767, rawX)))
        If Math.Abs(rawY) > AxisDeadzone Then thumbLY = CShort(Math.Max(-32768, Math.Min(32767, rawY)))



        ' 3. Right Stick (Logicool Dual Action: Z=RX, RotationZ=RY, or RotationX/Y)
        ' Increase deadzone to 12000 to prevent center drift / stuck inputs
        Const StickDeadzone As Integer = 12000

        ' Check Z axis for Right Stick X
        If state.Z > 0 Then
            Dim rawRX = CInt(state.Z) - 32768
            If Math.Abs(rawRX) > StickDeadzone Then
                thumbRX = CShort(Math.Max(-32768, Math.Min(32767, rawRX)))
            End If
        ElseIf state.RotationX > 0 Then
            Dim rawRX = CInt(state.RotationX) - 32768
            If Math.Abs(rawRX) > StickDeadzone Then
                thumbRX = CShort(Math.Max(-32768, Math.Min(32767, rawRX)))
            End If
        End If

        ' Check RotationZ / RotationY axis for Right Stick Y
        Dim rawRotYValue As Integer = 0
        If state.RotationZ > 0 Then
            rawRotYValue = state.RotationZ
        ElseIf state.RotationY > 0 Then
            rawRotYValue = state.RotationY
        End If

        If rawRotYValue > 0 Then
            Dim rawRY = 32767 - CInt(rawRotYValue)
            If Math.Abs(rawRY) > StickDeadzone Then
                thumbRY = CShort(Math.Max(-32768, Math.Min(32767, rawRY)))
            End If
        End If

        System.Diagnostics.Debug.WriteLine($"[DInput RStick] Raw (Z={state.Z}, RotX={state.RotationX}, RotY={state.RotationY}, RotZ={state.RotationZ}) -> Output (RX={thumbRX}, RY={thumbRY})")

        Dim buf(19) As Byte
        buf(0) = CByte(buttons And &HFF)
        buf(1) = CByte((buttons >> 8) And &HFF)
        buf(2) = leftTrigger
        buf(3) = rightTrigger
        BitConverter.GetBytes(thumbLX).CopyTo(buf, 4)
        BitConverter.GetBytes(thumbLY).CopyTo(buf, 6)
        BitConverter.GetBytes(thumbRX).CopyTo(buf, 8)
        BitConverter.GetBytes(thumbRY).CopyTo(buf, 10)
        Dim mk As UShort
        SyncLock _metaLock : mk = _metaKeys : End SyncLock
        BitConverter.GetBytes(mk).CopyTo(buf, 12)
        Return buf
    End Function

    Private Function IsXInputPacketActive(buf() As Byte) As Boolean
        Return IsDInputPacketActive(buf)
    End Function

    Private Function IsDInputPacketActive(buf() As Byte) As Boolean
        If buf Is Nothing OrElse buf.Length < 12 Then Return False
        Dim buttons As UShort = BitConverter.ToUInt16(buf, 0)
        If buttons <> 0 Then Return True
        If buf(2) > 0 OrElse buf(3) > 0 Then Return True
        Dim thumbLX As Short = BitConverter.ToInt16(buf, 4)
        Dim thumbLY As Short = BitConverter.ToInt16(buf, 6)
        Dim thumbRX As Short = BitConverter.ToInt16(buf, 8)
        Dim thumbRY As Short = BitConverter.ToInt16(buf, 10)
        If Math.Abs(CInt(thumbLX)) > 4000 OrElse Math.Abs(CInt(thumbLY)) > 4000 OrElse Math.Abs(CInt(thumbRX)) > 4000 OrElse Math.Abs(CInt(thumbRY)) > 4000 Then
            Return True
        End If
        Return False
    End Function

    Private Function IsDInputActionActive(state As JoystickState, action As String) As Boolean
        Dim mappedName = If(PadMapping.ContainsKey(action), PadMapping(action), action)
        Dim b = state.Buttons

        ' 1. Check Buttons
        Dim buttonIndex As Integer = -1
        Select Case mappedName
            Case "X" : buttonIndex = 0        ' Button 1
            Case "A" : buttonIndex = 1        ' Button 2
            Case "B" : buttonIndex = 2        ' Button 3
            Case "Y" : buttonIndex = 3        ' Button 4
            Case "LB" : buttonIndex = 4       ' Button 5
            Case "RB" : buttonIndex = 5       ' Button 6
            Case "LTrigger" : buttonIndex = 6 ' Button 7
            Case "RTrigger" : buttonIndex = 7 ' Button 8
            Case "Back" : buttonIndex = 8     ' Button 9
            Case "Start" : buttonIndex = 9    ' Button 10
            Case "LThumb" : buttonIndex = 10  ' Button 11
            Case "RThumb" : buttonIndex = 11  ' Button 12
        End Select

        If buttonIndex >= 0 AndAlso b.Length > buttonIndex AndAlso b(buttonIndex) Then
            Return True
        End If

        ' 2. Check POV (DPad)
        Dim povs = state.PointOfViewControllers
        If povs IsNot Nothing AndAlso povs.Length > 0 Then
            Dim pov = povs(0)
            If pov >= 0 AndAlso pov < 36000 Then
                Select Case mappedName
                    Case "DpadUp" : Return (pov >= 31500 OrElse pov <= 4500)
                    Case "DpadRight" : Return (pov >= 4500 AndAlso pov <= 13500)
                    Case "DpadDown" : Return (pov >= 13500 AndAlso pov <= 22500)
                    Case "DpadLeft" : Return (pov >= 22500 AndAlso pov <= 31500)
                End Select
            End If
        End If

        ' 3. Check Triggers thresholds (if mapped to axis)
        Select Case mappedName
            Case "LTrigger" : Return state.Z < 20000
            Case "RTrigger" : Return state.Z > 45000
        End Select

        Return False
    End Function

    Private Function IsKeyPressed(action As String) As Boolean
        If KeyMapping.ContainsKey(action) Then
            Dim k = KeyMapping(action)
            Return _pressedKeys.Contains(k)
        End If
        Return False
    End Function

    Public Sub [Stop]()
        _running = False
        Try
            If _dinputJoystick IsNot Nothing Then
                _dinputJoystick.Unacquire()
                _dinputJoystick.Dispose()
                _dinputJoystick = Nothing
            End If
            If _directInput IsNot Nothing Then
                _directInput.Dispose()
                _directInput = Nothing
            End If
        Catch
        End Try
        If _udpClient IsNot Nothing Then
            _udpClient.Close()
        End If
    End Sub

End Class