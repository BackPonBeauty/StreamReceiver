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

Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Threading
Imports System.Drawing
Imports System.Windows.Forms
Imports System.Configuration

Public Class Form1
    Inherits System.Windows.Forms.Form

    Private _firebase As New FirebaseMatchingClient()
    Private _discordUsername As String = ""
    Private _tooltip As New ToolTip()
    Private _lastTooltipItem As ListViewItem = Nothing
    Private _lastTooltipSubItemIndex As Integer = -1
    Private _hosts As Dictionary(Of String, HostInfo)
    Private _selectedHostId As String = ""
    Private _selectedSlot As Integer = 0

    Private _video As VideoReceiver
    Private _audio As AudioReceiver
    Private _renderer As DxRenderer
    Private _renderThread As Thread
    Private _running As Boolean = False
    Private _handshakeClient As UdpClient
    Private _xinput As XInputSender

    Private _portXInput As Integer
    Private _portHS As Integer
    Private _portVideo As Integer
    Private _portAudio As Integer

    Private _autoRefreshTimer As System.Windows.Forms.Timer
    Private _chatTimer As System.Windows.Forms.Timer

    Private ReadOnly W As Integer = 960
    Private ReadOnly H As Integer = 540

    Private pnlHostList As CyberPanel
    Private lvHosts As CyberListView
    Private btnRefresh As CyberButton
    Private pnlConnect As CyberPanel
    Private pnlChatOverlay As Panel
    Private rtbChatLog As TransparentChatLog
    Private txtChatInput As TextBox
    Private txtNick As TextBox
    Private lblSlot As Label
    Private cmbSlot As ComboBox
    Private btnConnect As CyberButton
    Private btnDisconnect As CyberButton
    Private btnLocalMode As CyberButton
    Private lblStatus As Label
    Private pnlVideo As Panel
    Private _ircNick As String = ""  ' streaming中の表示/非表示
    Private lnkPatreon As LinkLabel
    Private _chatSubscription As IDisposable = Nothing
    Private _connectionStartTime As Long = 0
    Private _isInitialLoading As Boolean = True
    Private _processedChatKeys As New HashSet(Of String)()

    Public _isLocalMode As Boolean = False

    Public Sub New()
        InitializeComponent()
        BuildCyberUI()
        AddHandler Me.Load, AddressOf Form1_Load
        AddHandler Me.Resize, AddressOf Form1_Resize
        AddHandler Me.FormClosing, AddressOf Form1_FormClosing
    End Sub

    Private Sub InitializeComponent()
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(Form1))
        Me.SuspendLayout()
        '
        'Form1
        '
        Me.BackColor = System.Drawing.Color.FromArgb(CType(CType(10, Byte), Integer), CType(CType(14, Byte), Integer), CType(CType(26, Byte), Integer))
        Me.ClientSize = New System.Drawing.Size(960, 540)
        Me.Font = New System.Drawing.Font("Consolas", 9.0!)
        Me.ForeColor = System.Drawing.Color.FromArgb(CType(CType(0, Byte), Integer), CType(CType(238, Byte), Integer), CType(CType(255, Byte), Integer))
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.Name = "Form1"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
        Me.Text = "STREAM RECEIVER"
        Me.ResumeLayout(False)

    End Sub

    Private Sub BuildCyberUI()
        pnlVideo = New Panel() With {
            .Location = New Point(0, 0),
            .Size = New Size(W, H),
            .BackColor = Color.Black,
            .Visible = False
        }

        pnlHostList = New CyberPanel() With {
            .Title = "HOST LIST",
            .Location = New Point(10, 14),
            .Size = New Size(940, 240)
        }

        btnRefresh = New CyberButton() With {
            .Text = "REFRESH",
            .Location = New Point(834, 12),
            .Size = New Size(94, 26),
            .GlowColor = Color.FromArgb(0, 180, 220),
            .Enabled = False
        }
        AddHandler btnRefresh.Click, AddressOf btnRefresh_Click

        lvHosts = New CyberListView() With {
            .Location = New Point(8, 44),
            .Size = New Size(924, 184)
        }
        lvHosts.Columns.Add("SERVER", 400)
        lvHosts.Columns.Add("P1", 124)
        lvHosts.Columns.Add("P2", 124)
        lvHosts.Columns.Add("P3", 124)
        lvHosts.Columns.Add("P4", 124)

        AddHandler lvHosts.RowClicked, AddressOf lvHosts_RowClicked
        AddHandler lvHosts.SlotClicked, AddressOf lvHosts_SlotClicked
        AddHandler lvHosts.MouseMove, AddressOf lvHosts_MouseMove

        pnlHostList.Controls.Add(btnRefresh)
        pnlHostList.Controls.Add(lvHosts)

        pnlConnect = New CyberPanel() With {
            .Title = "CONNECT",
            .Location = New Point(10, 264),
            .Size = New Size(940, 72)
        }

        lblSlot = New Label() With {
            .Text = "SLOT :",
            .Location = New Point(12, 22),
            .Size = New Size(52, 24),
            .ForeColor = Color.FromArgb(0, 180, 220),
            .Font = New Font("Consolas", 9),
            .TextAlign = ContentAlignment.MiddleLeft,
            .BackColor = Color.Transparent
        }

        cmbSlot = New ComboBox() With {
            .Location = New Point(66, 20),
            .Size = New Size(80, 24),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(3, 10, 22),
            .ForeColor = Color.FromArgb(0, 238, 255),
            .Font = New Font("Consolas", 9),
            .DropDownStyle = ComboBoxStyle.DropDownList
        }
        ' Local/WANトグルボタン
        btnLocalMode = New CyberButton() With {
    .Name = "btnLocalMode",
    .Text = "WAN",
    .Location = New Point(430, 20),
    .Size = New Size(80, 34),
    .GlowColor = Color.FromArgb(0, 180, 220),
    .ForeColor = Color.FromArgb(0, 180, 220)
}
        AddHandler btnLocalMode.Click, AddressOf btnLocalMode_Click
        pnlConnect.Controls.Add(btnLocalMode)

        btnConnect = New CyberButton() With {
            .Text = "CONNECT",
            .Location = New Point(600, 20),
            .Size = New Size(160, 34),
            .GlowColor = Color.FromArgb(0, 210, 80),
            .ForeColor = Color.FromArgb(0, 210, 80),
            .Enabled = False
        }
        AddHandler btnConnect.Click, AddressOf btnConnect_Click

        btnDisconnect = New CyberButton() With {
            .Text = "DISCONNECT",
            .Location = New Point(774, 20),
            .Size = New Size(160, 34),
            .GlowColor = Color.FromArgb(210, 40, 40),
            .ForeColor = Color.FromArgb(210, 40, 40),
            .Enabled = False
        }
        AddHandler btnDisconnect.Click, AddressOf btnDisconnect_Click

        pnlConnect.Controls.Add(lblSlot)
        pnlConnect.Controls.Add(cmbSlot)
        pnlConnect.Controls.Add(btnConnect)
        pnlConnect.Controls.Add(btnDisconnect)

        ' NICK ラベル (Connectパネル内に配置)
        Dim lblNick As New Label() With {
            .Text = "NICK :",
            .Location = New Point(230, 22),
            .Size = New Size(46, 20),
            .ForeColor = Color.FromArgb(0, 180, 220),
            .Font = New Font("Consolas", 8, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleLeft,
            .BackColor = Color.Transparent
        }

        ' ニックネーム入力 (Connectパネル内に配置)
        Dim savedNick = ConfigurationManager.AppSettings("IrcNick")
        If String.IsNullOrEmpty(savedNick) Then savedNick = "guest"
        txtNick = New TextBox() With {
            .Name = "txtNick",
            .Location = New Point(280, 20),
            .Size = New Size(120, 22),
            .BackColor = Color.FromArgb(3, 10, 22),
            .ForeColor = Color.FromArgb(0, 238, 255),
            .Font = New Font("Consolas", 9),
            .BorderStyle = BorderStyle.FixedSingle,
            .Text = savedNick,
            .ReadOnly = True
        }

        pnlConnect.Controls.Add(lblNick)
        pnlConnect.Controls.Add(txtNick)

        lblStatus = New Label() With {
            .Text = "INITIALIZING...",
            .Location = New Point(12, 530),
            .Size = New Size(936, 18),
            .ForeColor = Color.FromArgb(0, 180, 120),
            .Font = New Font("Consolas", 9),
            .TextAlign = ContentAlignment.MiddleLeft,
            .BackColor = Color.Transparent
        }

        Me.Controls.Add(pnlVideo)
        Me.Controls.Add(pnlHostList)
        Me.Controls.Add(pnlConnect)

        ' ステータスラベル（フォーム直下、pnlConnect内に重ねて表示）
        lblStatus = New Label() With {
            .Text = "INITIALIZING...",
            .Location = New Point(12, 464),
            .Size = New Size(936, 20),
            .ForeColor = Color.FromArgb(0, 180, 120),
            .Font = New Font("Consolas", 9),
            .TextAlign = ContentAlignment.MiddleLeft,
            .BackColor = Color.Transparent
        }
        Me.Controls.Add(lblStatus)

        ' Patreonリンク
        lnkPatreon = New LinkLabel() With {
            .Text = "Patreon.com/PonMi",
            .Font = New Font("Consolas", 9, FontStyle.Bold),
            .ForeColor = Color.FromArgb(255, 80, 160),
            .LinkColor = Color.FromArgb(0, 210, 255),
            .ActiveLinkColor = Color.FromArgb(255, 255, 255),
            .VisitedLinkColor = Color.FromArgb(0, 210, 255),
            .BackColor = Color.Transparent,
            .AutoSize = True
        }
        AddHandler lnkPatreon.LinkClicked, AddressOf lnkPatreon_LinkClicked
        Me.Controls.Add(lnkPatreon)

        ' Chat Overlay Panel (透過/右上オーバーレイ)
        pnlChatOverlay = New Panel() With {
            .Size = New Size(300, 110),
            .BackColor = Color.Transparent, ' 完全に背景を透明に！
            .Visible = False
        }

        rtbChatLog = New TransparentChatLog() With {
            .Dock = DockStyle.Fill,
            .ForeColor = Color.FromArgb(220, 220, 220),
            .Font = New Font("MS Gothic", 9.5F, FontStyle.Bold)
        }

        txtChatInput = New TextBox() With {
            .Dock = DockStyle.Bottom,
            .BackColor = Color.FromArgb(20, 30, 50),
            .ForeColor = Color.White,
            .BorderStyle = BorderStyle.FixedSingle,
            .Font = New Font("MS UI Gothic", 9),
            .Visible = False
        }

        AddHandler txtChatInput.KeyDown, AddressOf txtChatInput_KeyDown
        AddHandler txtChatInput.TextChanged, Sub() ResetChatTimer()

        _chatTimer = New System.Windows.Forms.Timer()
        _chatTimer.Interval = 7000
        AddHandler _chatTimer.Tick, AddressOf ChatTimer_Tick

        pnlChatOverlay.Controls.Add(rtbChatLog)
        pnlChatOverlay.Controls.Add(txtChatInput)
        pnlVideo.Controls.Add(pnlChatOverlay)

        ' 初期レイアウトを適用
        Form1_Resize(Nothing, EventArgs.Empty)
    End Sub

    Private Async Sub Form1_Load(sender As Object, e As EventArgs)
        AddHandler Me.KeyDown, AddressOf Form_KeyDown
        Me.KeyPreview = True

        pnlVideo.Width = W
        pnlVideo.Height = H
        pnlVideo.Location = New Point(0, 0)

        ' UI要素を一旦無効化
        pnlHostList.Enabled = False
        pnlConnect.Enabled = False

        ' --- 起動時 Discord 認証開始 ---
        SetStatus("Please complete Discord authentication...", Color.FromArgb(0, 180, 220))
        Dim authResult = Await DiscordAuth.AuthenticateAndCheckMembershipAsync(
            Sub(statusMsg)
                Me.Invoke(Sub() SetStatus(statusMsg, Color.FromArgb(0, 180, 220)))
            End Sub
        )

        If Not authResult.Success Then
            MessageBox.Show("An error occurred during Discord authentication." & vbCrLf & authResult.ErrorMessage, "Authentication Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            SetStatus("Discord authentication failed. Application will close.", Color.FromArgb(220, 60, 60))
            Application.Exit()
            Return
        End If

        If Not authResult.UserJoined Then
            Dim inviteUrl = DiscordAuth.GetInviteUrl()
            Dim msg = $"To connect to the host, you must join our Discord server.{vbCrLf}{vbCrLf}Current User: {authResult.Username}{vbCrLf}{vbCrLf}Would you like to join the server?"
            Dim dialogResult = MessageBox.Show(msg, "Not a Server Member", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            If dialogResult = DialogResult.Yes AndAlso Not String.IsNullOrEmpty(inviteUrl) AndAlso inviteUrl.StartsWith("http") Then
                Try
                    Process.Start(inviteUrl)
                Catch ex As Exception
                    MessageBox.Show("Could not open the invite URL in browser: " & ex.Message)
                End Try
            End If
            SetStatus("Access denied: Not a server member. Application will close.", Color.FromArgb(220, 140, 0))
            Application.Exit()
            Return
        End If

        ' 認証成功
        SetStatus($"Discord Auth Success: {authResult.Username}", Color.FromArgb(0, 220, 100))
        _discordUsername = authResult.Username

        ' NICK入力欄の初期値をDiscordユーザーネームに設定
        If txtNick IsNot Nothing Then
            txtNick.Text = authResult.Username
        End If

        ' UI要素を有効化
        pnlHostList.Enabled = True
        pnlConnect.Enabled = True
        ' --- 起動時 Discord 認証終了 ---

        SetStatus("Connecting to Firebase...", Color.FromArgb(100, 100, 100))
        Await _firebase.InitializeAsync()
        SetStatus("Fetching host list...", Color.FromArgb(100, 100, 100))
        Await LoadHostsAsync()

        _autoRefreshTimer = New System.Windows.Forms.Timer()
        _autoRefreshTimer.Interval = 60000
        AddHandler _autoRefreshTimer.Tick, AddressOf AutoRefresh_Tick
        _autoRefreshTimer.Start()

        If Not File.Exists("ffmpeg.exe") Then
            Dim result = MessageBox.Show(
                "ffmpeg.exe not found." & vbCrLf & vbCrLf &
                "Open the download page?" & vbCrLf &
                "(Place ffmpeg.exe in the same folder as StreamReceiver.exe)",
                "Error", MessageBoxButtons.YesNo, MessageBoxIcon.Error)
            If result = DialogResult.Yes Then Process.Start("https://www.gyan.dev/ffmpeg/builds/")
            Application.Exit()
        End If
    End Sub

    Private Async Function LoadHostsAsync() As Task
        _hosts = Await _firebase.GetActiveHostsAsync()
        lvHosts.Items.Clear()
        cmbSlot.Items.Clear()
        btnConnect.Enabled = False
        'btnConnect.Enabled = True
        If _hosts.Count = 0 Then
            SetStatus("No hosts found.", Color.FromArgb(220, 140, 0))
            Return
        End If

        For Each kvp In _hosts
            Dim host = kvp.Value
            Dim ping = Await MeasurePingAsync(host.Ip, 5001)
            Dim pingStr = If(ping >= 0, $"{ping}ms", "---")
            Dim namePart = If(String.IsNullOrEmpty(host.ServerName), host.Ip, host.ServerName)
            Dim displayName = If(String.IsNullOrEmpty(host.GameTitle),
                 $"{namePart}  [{pingStr}]",
                 $"{namePart}  [{host.GameTitle}]  {pingStr}")
            Dim item = New ListViewItem(displayName)
            item.Tag = kvp.Key
            For Each slot In {1, 2, 3, 4}
                If host.Slots IsNot Nothing AndAlso host.Slots.ContainsKey("slot" & slot.ToString()) Then
                    item.SubItems.Add(If(host.Slots("slot" & slot.ToString()).Available, "●", "×"))
                Else
                    item.SubItems.Add("")
                End If
            Next
            lvHosts.Items.Add(item)
        Next

        SetStatus($"{_hosts.Count} host(s) found.", Color.FromArgb(0, 200, 100))
    End Function

    Private Sub lvHosts_SlotClicked(slotIndex As Integer, item As ListViewItem)
        _selectedHostId = item.Tag.ToString()
        cmbSlot.Items.Clear()
        Dim host = _hosts(_selectedHostId)
        If host.Slots IsNot Nothing Then
            For Each kvp In host.Slots
                If kvp.Value.Available Then
                    cmbSlot.Items.Add($"P{kvp.Key.Replace("slot", "")}")
                End If
            Next
        End If
        Dim idx = cmbSlot.Items.IndexOf($"P{slotIndex}")
        If idx >= 0 Then
            cmbSlot.SelectedIndex = idx
            btnConnect.Enabled = True
            SetStatus($"Slot P{slotIndex} selected.", Color.FromArgb(0, 200, 255))
        End If
    End Sub

    Private Sub lvHosts_RowClicked(item As ListViewItem)
        _selectedHostId = item.Tag.ToString()
        cmbSlot.Items.Clear()
        Dim host = _hosts(_selectedHostId)
        If host.Slots Is Nothing Then Return
        For Each kvp In host.Slots
            If kvp.Value.Available Then
                cmbSlot.Items.Add($"P{kvp.Key.Replace("slot", "")}")
            End If
        Next
        If cmbSlot.Items.Count > 0 Then
            cmbSlot.SelectedIndex = 0
            btnConnect.Enabled = True
        Else
            btnConnect.Enabled = False
            SetStatus("No available slots.", Color.FromArgb(220, 140, 0))
        End If
    End Sub

    Private Async Sub btnRefresh_Click(sender As Object, e As EventArgs)
        btnRefresh.Enabled = False
        cmbSlot.Items.Clear()
        btnConnect.Enabled = False
        SetStatus("Refreshing...", Color.FromArgb(100, 100, 100))
        Await LoadHostsAsync()
        btnRefresh.Enabled = False
        If _autoRefreshTimer IsNot Nothing Then
            _autoRefreshTimer.Stop()
            _autoRefreshTimer.Start()
        End If
    End Sub

    Private Async Sub AutoRefresh_Tick(sender As Object, e As EventArgs)
        If _running Then Return
        Await LoadHostsAsync()
    End Sub

    Private Async Sub btnConnect_Click(sender As Object, e As EventArgs)
        If _selectedHostId = "" OrElse cmbSlot.SelectedItem Is Nothing Then Return
        Dim slotStr = cmbSlot.SelectedItem.ToString().Replace("P", "")
        _selectedSlot = CInt(slotStr)
        Dim slotKey = "slot" & slotStr
        Dim host = _hosts(_selectedHostId)
        Dim slotInfo = host.Slots(slotKey)
        _portXInput = (_selectedSlot - 1) * 4 + 5000
        _portHS = (_selectedSlot - 1) * 4 + 5001
        _portVideo = slotInfo.Video
        _portAudio = slotInfo.Audio
        Debug.WriteLine($"[CONNECT] IP={host.Ip} Slot=P{_selectedSlot} XInput={_portXInput} HS={_portHS} Video={_portVideo} Audio={_portAudio}")
        If Not _isLocalMode Then
            Await UPnPHelper.OpenPorts(_portXInput, _portHS, _portVideo, _portAudio)
        End If
        btnConnect.Enabled = False
        btnLocalMode.Enabled = False

        btnRefresh.Enabled = False
        SetStatus("Connecting...", Color.FromArgb(200, 200, 0))
        Dim t As New Thread(Sub() DoHandshake(host.Ip))
        t.IsBackground = True
        t.Start()
    End Sub

    Private Sub DoHandshake(ip As String)
        If _isLocalMode Then ip = "127.0.0.1"
        Try
            _handshakeClient = New UdpClient()
            Dim addresses = Dns.GetHostAddresses(ip)
            If addresses.Length = 0 Then Return
            Dim resolvedIP = addresses(0).ToString()
            Debug.WriteLine($"[DNS] {ip} -> {resolvedIP}")
            _handshakeClient.Connect(resolvedIP, _portHS)
            Dim hello() As Byte = System.Text.Encoding.ASCII.GetBytes("HELLO")
            Dim ep As New IPEndPoint(IPAddress.Any, 0)
            For i = 1 To 10
                _handshakeClient.Send(hello, hello.Length)
                _handshakeClient.Client.ReceiveTimeout = 1000
                Try
                    Dim recv() As Byte = _handshakeClient.Receive(ep)
                    Dim msg = System.Text.Encoding.ASCII.GetString(recv)
                    If msg.StartsWith("OK") Then
                        Dim parts = msg.Split(" "c)
                        Dim w As Integer = Me.W
                        Dim h As Integer = Me.H
                        If parts.Length >= 3 Then
                            Integer.TryParse(parts(1), w)
                            Integer.TryParse(parts(2), h)
                        End If
                        Dim codec As String = "H265"
                        If parts.Length >= 4 Then
                            codec = parts(3).Trim()
                        End If
                        Dim videoUdp As New UdpClient(_portVideo)
                        Dim audioUdp As New UdpClient(_portAudio)
                        Thread.Sleep(500)
                        Dim dummy As Byte() = {0, 0, 0, 0}
                        For ii = 1 To 10
                            videoUdp.Send(dummy, dummy.Length, New IPEndPoint(IPAddress.Parse(resolvedIP), _portVideo))
                            audioUdp.Send(dummy, dummy.Length, New IPEndPoint(IPAddress.Parse(resolvedIP), _portAudio))
                        Next
                        Thread.Sleep(200)
                        Me.Invoke(Sub()
                                      SetStatus("CONNECTED", Color.FromArgb(0, 220, 100))
                                      btnDisconnect.Enabled = True
                                      StartReceiving(w, h, videoUdp, audioUdp, codec)

                                      Dim nick = If(txtNick IsNot Nothing, txtNick.Text.Trim(), "")
                                      If String.IsNullOrEmpty(nick) Then nick = _ircNick
                                      If String.IsNullOrEmpty(nick) Then nick = "guest"
                                      Dim nameToWrite = If(String.IsNullOrEmpty(_discordUsername), nick, _discordUsername)
                                      Dim taskUpdate = UpdateFirebaseSlotUserAsync(nameToWrite)
                                      Dim taskJoinMsg = _firebase.SendChatMessageAsync(_selectedHostId, "SYSTEM", $"<P{_selectedSlot}><{nameToWrite}> joined")
                                  End Sub)
                        Dim hbThread As New Thread(Sub()
                                                       Do While _running
                                                           Try
                                                               Dim hb() As Byte = System.Text.Encoding.ASCII.GetBytes("HB")
                                                               _handshakeClient.Send(hb, hb.Length)
                                                           Catch ex As Exception
                                                               Debug.WriteLine("[HB] Error: " & ex.Message)
                                                           End Try
                                                           Thread.Sleep(2000)
                                                       Loop
                                                   End Sub)
                        hbThread.IsBackground = True
                        hbThread.Start()

                        Dim hsReceiveThread As New Thread(Sub()
                                                              Dim epRecv As New IPEndPoint(IPAddress.Any, 0)
                                                              _handshakeClient.Client.ReceiveTimeout = 2000
                                                              Do While _running
                                                                  Try
                                                                      Dim data() As Byte = _handshakeClient.Receive(epRecv)
                                                                      If data IsNot Nothing AndAlso data.Length > 0 Then
                                                                          Dim msgRecv = System.Text.Encoding.ASCII.GetString(data)
                                                                          If msgRecv.StartsWith("KICK") Then
                                                                              Me.Invoke(Sub()
                                                                                            If Me.FormBorderStyle = FormBorderStyle.None Then
                                                                                                Me.FormBorderStyle = FormBorderStyle.Sizable
                                                                                                Me.WindowState = FormWindowState.Normal
                                                                                                Me.ClientSize = New Size(w, h + 40)
                                                                                            End If
                                                                                            StopReceiving("kicked")
                                                                                            btnDisconnect.Enabled = False
                                                                                            btnConnect.Enabled = True
                                                                                            btnRefresh.Enabled = False
                                                                                            SetStatus("Kicked: no controller detected.", Color.FromArgb(220, 140, 0))
                                                                                            MessageBox.Show("You kicked from Host reason no controler detect", "Kicked", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                                                                                        End Sub)
                                                                              Return
                                                                          End If
                                                                      End If
                                                                  Catch ex As SocketException
                                                                      ' Timeout, just continue
                                                                  Catch ex As Exception
                                                                      Exit Do
                                                                  End Try
                                                              Loop
                                                          End Sub)
                        hsReceiveThread.IsBackground = True
                        hsReceiveThread.Start()

                        _xinput = New XInputSender()
                        AddHandler _xinput.ControllerDisconnected, AddressOf OnControllerDisconnected
                        _xinput.Start(resolvedIP, _portXInput, SharpDX.XInput.UserIndex.One)
                        Return
                    End If
                Catch ex As SocketException
                    Debug.WriteLine("[Handshake] SocketException: " & ex.Message)
                End Try
            Next
            Try
                _handshakeClient?.Close()
                _handshakeClient = Nothing
            Catch
            End Try
            Me.Invoke(Sub()
                          SetStatus("Connection failed.", Color.FromArgb(220, 60, 60))
                          btnConnect.Enabled = True
                          btnLocalMode.Enabled = True
                          btnRefresh.Enabled = False
                      End Sub)
        Catch ex As Exception
            Try
                _handshakeClient?.Close()
                _handshakeClient = Nothing
            Catch
            End Try
            Me.Invoke(Sub()
                          SetStatus("Error: " & ex.Message, Color.FromArgb(220, 60, 60))
                          btnConnect.Enabled = True
                          btnLocalMode.Enabled = True
                          btnRefresh.Enabled = False
                      End Sub)
        End Try
    End Sub

    Private Async Function MeasurePingAsync(ip As String, port As Integer) As Task(Of Integer)
        Try
            Using client As New UdpClient()
                client.Client.ReceiveTimeout = 1000
                Dim ep As New IPEndPoint(IPAddress.Any, 0)
                Dim total As Long = 0
                Dim count As Integer = 0

                For i = 1 To 2
                    Dim t1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    Dim msg = $"PING {t1}"
                    Dim data = System.Text.Encoding.ASCII.GetBytes(msg)
                    client.Send(data, data.Length, ip, port)

                    Try
                        Dim recv = client.Receive(ep)
                        Dim t2 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        ' RTT/2
                        total += (t2 - t1) \ 2
                        count += 1
                    Catch ex As SocketException
                        ' タイムアウト
                    End Try
                Next

                If count > 0 Then
                    Return CInt(total / count)
                End If
            End Using
        Catch
        End Try
        Return -1  ' 到達不可
    End Function

    Private Sub StartReceiving(w As Integer, h As Integer, udp4 As UdpClient, udp5 As UdpClient, codec As String)
        pnlHostList.Visible = False
        pnlConnect.Visible = False
        lblStatus.Visible = False
        lnkPatreon.Visible = False
        pnlVideo.Visible = True
        pnlVideo.BringToFront()

        ' チャットオーバーレイを表示して購読
        pnlChatOverlay.Visible = True
        pnlChatOverlay.Height = 0
        pnlChatOverlay.BringToFront()
        pnlChatOverlay.Left = pnlVideo.Width - pnlChatOverlay.Width - 10
        pnlChatOverlay.Top = 10
        rtbChatLog.Clear()
        _processedChatKeys.Clear()
        _isInitialLoading = True
        _connectionStartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        StartChatSubscription(_selectedHostId)
        Task.Run(Async Function()
                     Await Task.Delay(2000)
                     _isInitialLoading = False
                 End Function)

        Me.ClientSize = New Size(w, h)
        _renderer = New DxRenderer(pnlVideo.Handle, w, h)
        _video = New VideoReceiver(w, h, udp4, codec)
        _audio = New AudioReceiver(udp5)
        AddHandler _video.ServerDisconnected, Sub()
                                                  Me.Invoke(Sub()
                                                                StopReceiving()
                                                                btnDisconnect.Enabled = False
                                                                btnConnect.Enabled = True
                                                                btnRefresh.Enabled = False
                                                                SetStatus("Disconnected by host.", Color.FromArgb(150, 150, 150))
                                                            End Sub)
                                              End Sub
        _video.Start()
        _audio.Start()
        _running = True
        _renderThread = New Thread(AddressOf RenderLoop)
        _renderThread.IsBackground = True
        _renderThread.Priority = ThreadPriority.AboveNormal
        _renderThread.Start()
    End Sub

    Private Sub StopReceiving(Optional reason As String = "Disconnected")
        _chatTimer?.Stop()
        If Not String.IsNullOrEmpty(_selectedHostId) AndAlso _selectedSlot > 0 Then
            Dim nick = If(txtNick IsNot Nothing, txtNick.Text.Trim(), "")
            If String.IsNullOrEmpty(nick) Then nick = _ircNick
            If String.IsNullOrEmpty(nick) Then nick = "guest"
            Dim nameToWrite = If(String.IsNullOrEmpty(_discordUsername), nick, _discordUsername)
            Dim taskDisconnect = _firebase.SendChatMessageAsync(_selectedHostId, "SYSTEM", $"<P{_selectedSlot}><{nameToWrite}> {reason}")
        End If

        Me.ClientSize = New Size(960, 540)
        _running = False
        _xinput?.Stop()
        Thread.Sleep(100)
        _video?.Stop()
        _audio?.Stop()
        _renderer?.Dispose()
        _renderer = Nothing
        _video = Nothing
        _audio = Nothing
        Try
            _handshakeClient?.Close()
            _handshakeClient = Nothing
        Catch
        End Try
        pnlVideo.Visible = False
        pnlHostList.Visible = True
        pnlConnect.Visible = True
        lblStatus.Visible = True
        lnkPatreon.Visible = True
        btnLocalMode.Enabled = True

        ' チャット購読の解除
        pnlChatOverlay.Visible = False
        StopChatSubscription()

        Form1_Resize(Nothing, EventArgs.Empty)
    End Sub

    Private Async Sub OnControllerDisconnected()
        Me.Invoke(Sub()

                      StopReceiving()
                      btnDisconnect.Enabled = False
                      btnConnect.Enabled = True
                      btnRefresh.Enabled = False
                      SetStatus("Disconnected: no controller detected.", Color.FromArgb(220, 140, 0))
                  End Sub)
        Await CleanUpFirebaseSlotAsync()
        If Not _isLocalMode Then
            Await UPnPHelper.ClosePorts(_portXInput, _portHS, _portVideo, _portAudio)
        End If
    End Sub

    Private Sub RenderLoop()
        Do While _running
            If _video IsNot Nothing Then
                Dim frame(_video.FrameSize - 1) As Byte
                _video.TryGetFrame(frame)
                _renderer?.DrawFrame(frame)
            End If
        Loop
    End Sub

    Private Async Sub btnDisconnect_Click(sender As Object, e As EventArgs)
        StopReceiving()
        Await CleanUpFirebaseSlotAsync()
        If Not _isLocalMode Then
            Await UPnPHelper.ClosePorts(_portXInput, _portHS, _portVideo, _portAudio)
        End If
        btnDisconnect.Enabled = False
        btnConnect.Enabled = True
        btnRefresh.Enabled = False
        SetStatus("Disconnected.", Color.FromArgb(150, 150, 150))
    End Sub

    Private Async Sub Form_KeyDown(sender As Object, e As KeyEventArgs)
        If e.KeyCode = Keys.F11 Then
            If Me.WindowState = FormWindowState.Maximized AndAlso Me.FormBorderStyle = FormBorderStyle.None Then
                Me.FormBorderStyle = FormBorderStyle.Sizable
                Me.WindowState = FormWindowState.Normal
            Else
                Me.FormBorderStyle = FormBorderStyle.None
                Me.WindowState = FormWindowState.Maximized
            End If
            Return
        End If

        If e.KeyCode = Keys.Enter Then
            If _running AndAlso Not txtChatInput.Visible Then
                pnlChatOverlay.Height = 110
                txtChatInput.Visible = True
                txtChatInput.Focus()
                ResetChatTimer()
                e.Handled = True
                e.SuppressKeyPress = True
                Return
            End If
        End If

        If e.KeyCode = Keys.Escape Then
            If Me.FormBorderStyle = FormBorderStyle.None Then
                Me.FormBorderStyle = FormBorderStyle.Sizable
                Me.WindowState = FormWindowState.Normal
                Me.ClientSize = New Size(W, H + 40)
            ElseIf _running Then
                Me.WindowState = FormWindowState.Normal
                StopReceiving()
                Await CleanUpFirebaseSlotAsync()
                If Not _isLocalMode Then
                    Await UPnPHelper.ClosePorts(_portXInput, _portHS, _portVideo, _portAudio)
                End If
                btnDisconnect.Enabled = False
                btnConnect.Enabled = False
                SetStatus("Disconnected.", Color.FromArgb(150, 150, 150))
                Await LoadHostsAsync()
            Else
                Me.Close()
            End If
        End If
    End Sub

    Private Sub Form1_Resize(sender As Object, e As EventArgs)
        Dim cw = Me.ClientSize.Width
        Dim ch = Me.ClientSize.Height

        If pnlVideo IsNot Nothing AndAlso pnlVideo.Visible Then
            ' 映像パネル：16:9維持
            Dim panelH = ch
            Dim panelW = CInt(panelH * 16.0 / 9.0)
            If panelW > cw Then
                panelW = cw
                panelH = CInt(panelW * 9.0 / 16.0)
            End If
            pnlVideo.Width = panelW
            pnlVideo.Height = panelH
            pnlVideo.Left = (cw - panelW) \ 2
            pnlVideo.Top = 0

            ' チャットオーバーレイ：映像右上に追従
            If pnlChatOverlay IsNot Nothing Then
                pnlChatOverlay.Left = pnlVideo.Width - pnlChatOverlay.Width - 10
                pnlChatOverlay.Top = 10
            End If
        Else
            ' 通常UI：各パネルを幅に追従
            Dim margin = 10
            Dim pw = cw - margin * 2

            If pnlHostList IsNot Nothing Then
                pnlHostList.Width = pw
                pnlHostList.Height = ch - 14 - 72 - 20 - margin * 4
                If lvHosts IsNot Nothing Then
                    lvHosts.Width = pnlHostList.Width - 16
                    lvHosts.Height = pnlHostList.Height - 48
                    ' HOST IP列を可変幅、P1〜P4は固定
                    Dim slotColW = 80
                    Dim ipColW = lvHosts.Width - slotColW * 4 - 4
                    If lvHosts.Columns.Count >= 5 Then
                        lvHosts.Columns(0).Width = ipColW
                        For i = 1 To 4
                            lvHosts.Columns(i).Width = slotColW
                        Next
                    End If
                End If
                If btnRefresh IsNot Nothing Then
                    btnRefresh.Left = pnlHostList.Width - btnRefresh.Width - 12
                End If
            End If

            If pnlConnect IsNot Nothing Then
                pnlConnect.Width = pw
                pnlConnect.Top = pnlHostList.Top + pnlHostList.Height + margin
                If btnConnect IsNot Nothing Then
                    btnConnect.Left = pw - 160 - 160 - 16
                End If
                If btnDisconnect IsNot Nothing Then
                    btnDisconnect.Left = pw - 160 - 8
                End If
            End If

            If lblStatus IsNot Nothing Then
                lblStatus.Top = pnlConnect.Top + pnlConnect.Height + 12
                lblStatus.Width = pw - 200
            End If

            If lnkPatreon IsNot Nothing Then
                lnkPatreon.Top = pnlConnect.Top + pnlConnect.Height + 12
                lnkPatreon.Left = cw - 10 - lnkPatreon.Width
            End If

            If pnlChatOverlay IsNot Nothing Then
                pnlChatOverlay.Left = pnlVideo.Width - pnlChatOverlay.Width - 10
                pnlChatOverlay.Top = 10
            End If

        End If
    End Sub

    Private Sub SetStatus(msg As String, color As Color)
        Dim action As Action = Sub()
                                   lblStatus.Text = msg
                                   lblStatus.ForeColor = color
                               End Sub
        If Me.IsHandleCreated AndAlso Me.InvokeRequired Then
            Me.BeginInvoke(action)
        Else
            action()
        End If
    End Sub

    Private Sub btnLocalMode_Click(sender As Object, e As EventArgs)
        _isLocalMode = Not _isLocalMode
        Dim btn = DirectCast(sender, CyberButton)
        If _isLocalMode Then
            btn.Text = "LOCAL"
            btn.GlowColor = Color.FromArgb(255, 160, 0)
            btn.ForeColor = Color.FromArgb(255, 160, 0)
        Else
            btn.Text = "WAN"
            btn.GlowColor = Color.FromArgb(0, 180, 220)
            btn.ForeColor = Color.FromArgb(0, 180, 220)
        End If
    End Sub

    Private Async Sub txtChatInput_KeyDown(sender As Object, e As KeyEventArgs)
        If e.KeyCode = Keys.Enter Then
            Dim msg = txtChatInput.Text.Trim()

            ' Suppress Enter key to prevent beep sound
            e.Handled = True
            e.SuppressKeyPress = True

            If String.IsNullOrEmpty(msg) Then
                ' 入力欄がnothingだと height = 0
                _chatTimer?.Stop()
                txtChatInput.Text = ""
                txtChatInput.Visible = False
                pnlChatOverlay.Height = 0
                pnlVideo.Focus()
            Else
                ' 入力欄に文字があれば送信 heightそのまま
                txtChatInput.Text = ""
                txtChatInput.Visible = False
                pnlVideo.Focus()
                ResetChatTimer()

                Dim nick = If(txtNick IsNot Nothing, txtNick.Text.Trim(), "")
                If String.IsNullOrEmpty(nick) Then nick = _ircNick
                If String.IsNullOrEmpty(nick) Then nick = "guest"

                ' Save nickname to App.config
                Try
                    Dim config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None)
                    config.AppSettings.Settings("IrcNick").Value = nick
                    config.Save(ConfigurationSaveMode.Modified)
                    ConfigurationManager.RefreshSection("appSettings")
                Catch : End Try

                ' Send to firebase asynchronously
                Await _firebase.SendChatMessageAsync(_selectedHostId, nick, msg)
            End If
        End If
    End Sub

    Private Sub StartChatSubscription(hostId As String)
        StopChatSubscription()
        _chatSubscription = _firebase.GetChatObservable(hostId).Subscribe(
            Sub(chatEvent)
                If chatEvent.Object IsNot Nothing AndAlso chatEvent.EventType = Firebase.Database.Streaming.FirebaseEventType.InsertOrUpdate Then
                    Me.Invoke(Sub()
                                  Dim key = chatEvent.Key
                                  If _processedChatKeys.Contains(key) Then Return
                                  _processedChatKeys.Add(key)

                                  Dim isNewMessage = Not _isInitialLoading
                                  Dim msgText = ""
                                  If String.IsNullOrEmpty(chatEvent.Object.Username) OrElse chatEvent.Object.Username = "SYSTEM" Then
                                      msgText = chatEvent.Object.Message
                                  Else
                                      msgText = $"{chatEvent.Object.Username}: {chatEvent.Object.Message}"
                                  End If
                                  AppendChat(msgText, isNewMessage)
                              End Sub)
                End If
            End Sub,
            Sub(ex) Debug.WriteLine("[ERROR] Chat subscription error: " & ex.Message)
        )
    End Sub

    Private Sub StopChatSubscription()
        _chatSubscription?.Dispose()
        _chatSubscription = Nothing
    End Sub

    Private Sub AppendChat(msg As String, openOverlay As Boolean)
        If rtbChatLog.InvokeRequired Then
            rtbChatLog.Invoke(Sub() AppendChat(msg, openOverlay))
            Return
        End If
        rtbChatLog.AppendChat(msg)

        If openOverlay Then
            ' 新しいメッセージを受信したら、heightを110に、5秒で自動で height = 0に
            pnlChatOverlay.Height = 110
            ResetChatTimer()
        End If
    End Sub

    Private Sub ChatTimer_Tick(sender As Object, e As EventArgs)
        _chatTimer?.Stop()
        txtChatInput.Visible = False
        txtChatInput.Text = ""
        pnlChatOverlay.Height = 0
        If pnlVideo.Visible Then
            pnlVideo.Focus()
        End If
    End Sub

    Private Sub ResetChatTimer()
        _chatTimer?.Stop()
        _chatTimer?.Start()
    End Sub

    ' -------------------------------------------------------
    ' Firebase Chat
    ' -------------------------------------------------------

    Private Async Function UpdateFirebaseSlotUserAsync(username As String) As Task
        If Not String.IsNullOrEmpty(_selectedHostId) AndAlso _selectedSlot > 0 Then
            Await _firebase.UpdateSlotUserAsync(_selectedHostId, _selectedSlot, username)
        End If
    End Function

    Private Async Function CleanUpFirebaseSlotAsync() As Task
        If Not String.IsNullOrEmpty(_selectedHostId) AndAlso _selectedSlot > 0 Then
            Await _firebase.UpdateSlotUserAsync(_selectedHostId, _selectedSlot, Nothing)
        End If
    End Function

    Private Sub lvHosts_MouseMove(sender As Object, e As MouseEventArgs)
        Dim hit = lvHosts.HitTest(e.X, e.Y)
        If hit.Item IsNot Nothing Then
            Dim colIndex As Integer = -1
            Dim cumX As Integer = 0
            For i = 0 To lvHosts.Columns.Count - 1
                cumX += lvHosts.Columns(i).Width
                If e.X < cumX Then
                    colIndex = i
                    Exit For
                End If
            Next

            If colIndex >= 1 AndAlso colIndex <= 4 Then
                If hit.Item IsNot _lastTooltipItem OrElse colIndex <> _lastTooltipSubItemIndex Then
                    _lastTooltipItem = hit.Item
                    _lastTooltipSubItemIndex = colIndex

                    Dim hostId = hit.Item.Tag.ToString()
                    If _hosts IsNot Nothing AndAlso _hosts.ContainsKey(hostId) Then
                        Dim host = _hosts(hostId)
                        Dim slotKey = "slot" & colIndex.ToString()
                        If host.Slots IsNot Nothing AndAlso host.Slots.ContainsKey(slotKey) Then
                            Dim slot = host.Slots(slotKey)
                            Dim username = slot.User
                            If Not String.IsNullOrEmpty(username) Then
                                _tooltip.Show($"User: {username}", lvHosts, e.X + 15, e.Y + 15, 3000)
                            Else
                                _tooltip.Hide(lvHosts)
                            End If
                        Else
                            _tooltip.Hide(lvHosts)
                        End If
                    Else
                        _tooltip.Hide(lvHosts)
                    End If
                End If
                Return
            End If
        End If

        If _lastTooltipItem IsNot Nothing OrElse _lastTooltipSubItemIndex <> -1 Then
            _tooltip.Hide(lvHosts)
            _lastTooltipItem = Nothing
            _lastTooltipSubItemIndex = -1
        End If
    End Sub

    Private Sub lnkPatreon_LinkClicked(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Try
            Process.Start("https://patreon.com/PonMi")
        Catch ex As Exception
            MessageBox.Show("Failed to open Patreon URL: " & ex.Message)
        End Try
    End Sub

    Private Sub Form1_FormClosing(sender As Object, e As FormClosingEventArgs)
        _autoRefreshTimer?.Stop()
        StopReceiving()

        If Not String.IsNullOrEmpty(_selectedHostId) AndAlso _selectedSlot > 0 Then
            Try
                Task.Run(Async Function()
                             Await CleanUpFirebaseSlotAsync()
                             If Not _isLocalMode Then
                                 Await UPnPHelper.ClosePorts(_portXInput, _portHS, _portVideo, _portAudio)
                             End If
                         End Function).Wait(1500)
            Catch
            End Try
        End If
    End Sub

End Class