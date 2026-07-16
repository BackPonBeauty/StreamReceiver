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

    Shared Sub New()
        AddHandler AppDomain.CurrentDomain.AssemblyResolve, AddressOf ResolveAssemblies
    End Sub

    Private Shared Function ResolveAssemblies(sender As Object, args As ResolveEventArgs) As System.Reflection.Assembly
        Dim folderPath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dll")
        Dim assemblyName As New System.Reflection.AssemblyName(args.Name)
        Dim assemblyPath As String = Path.Combine(folderPath, assemblyName.Name & ".dll")

        If File.Exists(assemblyPath) Then
            Return System.Reflection.Assembly.LoadFrom(assemblyPath)
        End If
        Return Nothing
    End Function

    Private _firebase As New FirebaseMatchingClient()
    Private _sessionId As String = Guid.NewGuid().ToString("N").Substring(0, 8)
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

    Private Enum VideoDisplayMode
        Normal = 0
        TopLeft = 1
        TopRight = 2
        BottomLeft = 3
        BottomRight = 4
    End Enum
    Private _displayMode As VideoDisplayMode = VideoDisplayMode.Normal
    Private _lastClickTime As DateTime = DateTime.MinValue
    Private _lastClickPoint As Point = Point.Empty

    Private _portXInput As Integer
    Private _portHS As Integer
    Private _portVideo As Integer
    Private _portAudio As Integer

    Private _autoRefreshTimer As System.Windows.Forms.Timer
    Private _chatTimer As System.Windows.Forms.Timer
    Private _telemetryTimer As System.Windows.Forms.Timer

    Private ReadOnly W As Integer = 960
    Private ReadOnly H As Integer = 540

    ' Aspect ratio from host handshake (updated on connect, reset on disconnect)
    Private _streamW As Integer = 960
    Private _streamH As Integer = 540
    Private _useH265 As Boolean = True   ' True=H265優先, False=H264強制
    Private btnH265 As CyberButton

    Private pnlHostList As CyberPanel
    Private lvHosts As CyberListView
    Private btnRefresh As CyberButton
    Private pnlConnect As CyberPanel
    Private pnlChatOverlay As Panel
    Private rtbChatLog As TransparentChatLog
    Private txtChatInput As TextBox
    Private txtNick As TextBox
    Private txtLocalIp As TextBox
    Private lblLocalIp As Label
    Private lblSlot As Label
    Private cmbSlot As ComboBox
    Private btnConnect As CyberButton
    Private btnDisconnect As CyberButton
    Private btnLocalMode As CyberButton
    Private btnOption As CyberButton
    Private lblStatus As Label
    Private pnlVideo As Panel
    Private _ircNick As String = ""  ' streaming中の表示/非表示
    Private lnkPatreon As LinkLabel
    Private btnSponsor As CyberButton
    Private _chatSubscription As IDisposable = Nothing
    Private _hostsSubscription As IDisposable = Nothing
    Private _pingCache As New Dictionary(Of String, String)()
    Private _connectionStartTime As Long = 0
    Private _isInitialLoading As Boolean = True
    Private _processedChatKeys As New HashSet(Of String)()

    Public _isLocalMode As Boolean = False
    Private version_s As String = "20260713" 'haaaaaaaaaaaaaaaaaaaaaaaaaaaaa

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
        Me.Text = "STREAM RECEIVER V20260713"
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
        ' H265トグルボタン
        btnH265 = New CyberButton() With {
            .Name = "btnH265",
            .Text = "H265",
            .Location = New Point(774, 20),
            .Size = New Size(160, 34),
            .GlowColor = Color.FromArgb(0, 210, 180),
            .ForeColor = Color.FromArgb(0, 210, 180)
        }
        AddHandler btnH265.Click, AddressOf btnH265_Click
        pnlConnect.Controls.Add(btnH265)

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

        ' Option/KeyConfig button
        btnOption = New CyberButton() With {
            .Name = "btnOption",
            .Text = "OPTION",
            .Location = New Point(515, 20),
            .Size = New Size(75, 34),
            .GlowColor = Color.FromArgb(0, 180, 220),
            .ForeColor = Color.FromArgb(0, 180, 220)
        }
        AddHandler btnOption.Click, AddressOf btnOption_Click
        pnlConnect.Controls.Add(btnOption)

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

        ' NICK ラベル (Connectパネル内に配置)
        Dim lblNick As New Label() With {
            .Text = "NICK:",
            .Location = New Point(152, 22),
            .Size = New Size(40, 20),
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
            .Location = New Point(194, 20),
            .Size = New Size(85, 22),
            .BackColor = Color.FromArgb(3, 10, 22),
            .ForeColor = Color.FromArgb(0, 238, 255),
            .Font = New Font("Consolas", 9),
            .BorderStyle = BorderStyle.FixedSingle,
            .Text = savedNick,
            .ReadOnly = True
        }

        ' Local IP ラベル
        lblLocalIp = New Label() With {
            .Text = "IP:",
            .Location = New Point(285, 22),
            .Size = New Size(24, 20),
            .ForeColor = Color.FromArgb(0, 180, 220),
            .Font = New Font("Consolas", 8, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleLeft,
            .BackColor = Color.Transparent,
            .Visible = False
        }

        ' Local IP 入力欄 (デフォルト "127.0.0.1")
        txtLocalIp = New TextBox() With {
            .Name = "txtLocalIp",
            .Location = New Point(310, 20),
            .Size = New Size(110, 22),
            .BackColor = Color.FromArgb(3, 10, 22),
            .ForeColor = Color.FromArgb(255, 160, 0),
            .Font = New Font("Consolas", 9),
            .BorderStyle = BorderStyle.FixedSingle,
            .Text = "127.0.0.1",
            .Visible = False
        }

        pnlConnect.Controls.Add(lblNick)
        pnlConnect.Controls.Add(txtNick)
        pnlConnect.Controls.Add(lblLocalIp)
        pnlConnect.Controls.Add(txtLocalIp)

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

        ' Sponsor Button
        btnSponsor = New CyberButton() With {
            .Name = "btnSponsor",
            .Text = "SPONSOR",
            .Size = New Size(90, 22),
            .GlowColor = Color.FromArgb(255, 80, 160),
            .ForeColor = Color.FromArgb(255, 80, 160)
        }
        AddHandler btnSponsor.Click, AddressOf btnSponsor_Click
        Me.Controls.Add(btnSponsor)

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
        Me.Controls.Add(pnlChatOverlay)

        ' 初期レイアウトを適用
        Form1_Resize(Nothing, EventArgs.Empty)
    End Sub

    Private Async Sub Form1_Load(sender As Object, e As EventArgs)
        AddHandler Me.KeyDown, AddressOf Form_KeyDown
        AddHandler Me.KeyUp, AddressOf Form_KeyUp
        Me.KeyPreview = True
        XInputSender.LoadConfig()

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

        ' --- Version check ---
        Try
            Dim remoteVersion = Await _firebase.GetRemoteVersionAsync("clientversion")
            If Not String.IsNullOrEmpty(remoteVersion) Then
                Dim local As Integer = 0
                Dim remote As Integer = 0
                Integer.TryParse(version_s, local)
                Integer.TryParse(remoteVersion, remote)
                If remote > local Then
                    Dim dlgResult = MessageBox.Show(
                        "新しいバージョン(" & remoteVersion & ")があります。ダウンロードしますか？" & vbCrLf & "「いいえ」で現在のバージョンのまま起動します。",
                        "Update Available", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                    If dlgResult = DialogResult.Yes Then
                        Process.Start("https://github.com/BackPonBeauty/Supermodel3-PonMi-Streaming")
                    End If
                    Application.Exit()
                    Return
                End If
            End If
        Catch ex As Exception
            Debug.WriteLine("[ERROR] Version check failed: " & ex.Message)
        End Try

        SetStatus("Subscribing to host list...", Color.FromArgb(100, 100, 100))
        If btnRefresh IsNot Nothing Then btnRefresh.Visible = False
        StartHostsSubscription()

        SetStatus("Fetching host list...", Color.FromArgb(100, 100, 100))
        Dim initialHosts = Await _firebase.GetActiveHostsAsync()
        _hosts = If(initialHosts, New Dictionary(Of String, HostInfo)())
        UpdateHostsListView()

        ' ffmpeg 自動チェック＆ダウンロード
        If FfmpegHelper.GetFfmpegPath() Is Nothing Then
            SetStatus("⏬ ffmpeg をダウンロードしています...", Color.Cyan)
            Dim prog = New Progress(Of String)(Sub(msg) SetStatus(msg, Color.Cyan))
            Dim ok = Await FfmpegHelper.EnsureFfmpegAsync(prog)
            If Not ok Then
                MessageBox.Show(
                    "ffmpeg のダウンロードに失敗しました。" & vbCrLf &
                    "手動でダウンロードして同じフォルダに配置してください。" & vbCrLf &
                    "https://www.gyan.dev/ffmpeg/builds/",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Application.Exit()
                Return
            End If
            SetStatus("ffmpeg の準備が完了しました", Color.FromArgb(0, 220, 100))
        End If
    End Sub

    Private Sub StartHostsSubscription()
        _hostsSubscription?.Dispose()
        If _hosts Is Nothing Then
            _hosts = New Dictionary(Of String, HostInfo)()
        End If
        _hostsSubscription = _firebase.GetHostsObservable().Subscribe(
            Sub(hostEvent)
                If hostEvent.Object IsNot Nothing Then
                    Dim key = hostEvent.Key
                    Me.Invoke(Sub()
                                  If hostEvent.EventType = Firebase.Database.Streaming.FirebaseEventType.InsertOrUpdate Then
                                      _hosts(key) = hostEvent.Object
                                  ElseIf hostEvent.EventType = Firebase.Database.Streaming.FirebaseEventType.Delete Then
                                      If _hosts.ContainsKey(key) Then _hosts.Remove(key)
                                  End If
                                  UpdateHostsListView()
                              End Sub)
                End If
            End Sub,
            Sub(ex) Debug.WriteLine("[ERROR] Hosts subscription error: " & ex.Message)
        )
    End Sub

    Private Sub UpdateHostsListView()
        If lvHosts.InvokeRequired Then
            Me.Invoke(Sub() UpdateHostsListView())
            Return
        End If

        Dim prevSelectedHostId = _selectedHostId
        Dim prevSelectedSlotText = If(cmbSlot.SelectedItem IsNot Nothing, cmbSlot.SelectedItem.ToString(), "")

        lvHosts.Items.Clear()
        cmbSlot.Items.Clear()
        btnConnect.Enabled = False

        Dim nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        Dim timeoutMs = 10 * 60 * 1000L

        Dim activeCount As Integer = 0

        For Each kvp In _hosts
            Dim hostId = kvp.Key
            Dim host = kvp.Value

            Dim elapsed = nowUnix - host.Timestamp
            If elapsed > timeoutMs Then Continue For

            activeCount += 1
            Dim ip = host.Ip
            Dim namePart = If(String.IsNullOrEmpty(host.ServerName), ip, host.ServerName)

            Dim pingStr As String = "---"
            If _pingCache.ContainsKey(ip) Then
                pingStr = _pingCache(ip)
            End If

            ' トリガーされていない新規IPの場合のみ、バックグラウンドでPing計測を非同期開始
            If Not _pingCache.ContainsKey(ip) Then
                _pingCache(ip) = "Measuring..."
                Dim targetIp = ip
                Task.Run(Async Function()
                             Dim ping As Integer = -1
                             For Each testPort As Integer In {55001, 55005, 55009, 55013}
                                 ping = Await MeasurePingAsync(targetIp, testPort)
                                 If ping >= 0 Then Exit For
                             Next
                             Dim pStr = If(ping >= 0, $"{ping}ms", "---")
                             Me.BeginInvoke(Sub()
                                                _pingCache(targetIp) = pStr
                                                UpdateHostsListView()
                                            End Sub)
                         End Function)
            End If

            Dim displayName = If(String.IsNullOrEmpty(host.GameTitle),
                 $"{namePart}  [{pingStr}]",
                 $"{namePart}  [{host.GameTitle}]  {pingStr}")

            Dim item = New ListViewItem(displayName)
            item.Tag = hostId

            For Each slot In {1, 2, 3, 4}
                Dim slotKey = "slot" & slot.ToString()
                If host.Slots IsNot Nothing AndAlso host.Slots.ContainsKey(slotKey) Then
                    Dim slotInfo = host.Slots(slotKey)
                    If slotInfo.Available Then
                        item.SubItems.Add("●" & slotInfo.ClientCount.ToString())
                    Else
                        item.SubItems.Add("×")
                    End If
                Else
                    item.SubItems.Add("")
                End If
            Next

            lvHosts.Items.Add(item)

            If hostId = prevSelectedHostId Then
                _selectedHostId = hostId
                If host.Slots IsNot Nothing Then
                    For Each slotKvp In host.Slots
                        If slotKvp.Value.Available Then
                            Dim uCount = If(Not String.IsNullOrEmpty(slotKvp.Value.Player), 1, 0) +
                                         If(Not String.IsNullOrEmpty(slotKvp.Value.Spectator), 1, 0)
                            If slotKvp.Value.ClientCount < 2 AndAlso uCount < 2 Then
                                cmbSlot.Items.Add($"P{slotKvp.Key.Replace("slot", "")}")
                            End If
                        End If
                    Next
                End If
                If Not String.IsNullOrEmpty(prevSelectedSlotText) Then
                    Dim idx = cmbSlot.Items.IndexOf(prevSelectedSlotText)
                    If idx >= 0 Then
                        cmbSlot.SelectedIndex = idx
                        btnConnect.Enabled = True
                    End If
                End If
            End If
        Next

        If activeCount = 0 Then
            SetStatus("No hosts found.", Color.FromArgb(220, 140, 0))
        Else
            SetStatus($"{activeCount} host(s) found.", Color.FromArgb(0, 200, 100))
        End If
    End Sub

    Private Sub lvHosts_SlotClicked(slotIndex As Integer, item As ListViewItem)
        _selectedHostId = item.Tag.ToString()
        cmbSlot.Items.Clear()
        Dim host = _hosts(_selectedHostId)

        Dim slotKey = "slot" & slotIndex.ToString()
        If host.Slots IsNot Nothing AndAlso host.Slots.ContainsKey(slotKey) Then
            Dim slotInfo = host.Slots(slotKey)
            Dim uCount = If(Not String.IsNullOrEmpty(slotInfo.Player), 1, 0) +
                         If(Not String.IsNullOrEmpty(slotInfo.Spectator), 1, 0)
            If slotInfo.ClientCount >= 2 OrElse uCount >= 2 Then
                btnConnect.Enabled = False
                SetStatus($"Slot P{slotIndex} is full (2 or more users). Connection blocked.", Color.FromArgb(255, 60, 60))
                Return
            End If
        End If

        If host.Slots IsNot Nothing Then
            For Each kvp In host.Slots
                Dim uCount = If(Not String.IsNullOrEmpty(kvp.Value.Player), 1, 0) +
                             If(Not String.IsNullOrEmpty(kvp.Value.Spectator), 1, 0)
                If kvp.Value.Available AndAlso kvp.Value.ClientCount < 2 AndAlso uCount < 2 Then
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
                Dim uCount = If(Not String.IsNullOrEmpty(kvp.Value.Player), 1, 0) +
                             If(Not String.IsNullOrEmpty(kvp.Value.Spectator), 1, 0)
                If uCount < 2 AndAlso kvp.Value.ClientCount < 2 Then
                    cmbSlot.Items.Add($"P{kvp.Key.Replace("slot", "")}")
                End If
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

    Private Sub btnRefresh_Click(sender As Object, e As EventArgs)
        UpdateHostsListView()
    End Sub

    Private Sub AutoRefresh_Tick(sender As Object, e As EventArgs)
    End Sub

    Private Async Sub btnConnect_Click(sender As Object, e As EventArgs)
        If _selectedHostId = "" OrElse cmbSlot.SelectedItem Is Nothing Then Return

        ' ffmpeg 存在チェック → なければ自動ダウンロード
        If FfmpegHelper.GetFfmpegPath() Is Nothing Then
            btnConnect.Enabled = False
            SetStatus("⏬ ffmpeg をダウンロードしています...", Color.Cyan)
            Dim prog = New Progress(Of String)(Sub(msg) SetStatus(msg, Color.Cyan))
            Dim ok = Await FfmpegHelper.EnsureFfmpegAsync(prog)
            If Not ok Then
                SetStatus("❌ ffmpeg のダウンロードに失敗しました", Color.Red)
                btnConnect.Enabled = True
                Return
            End If
        End If

        Dim slotStr = cmbSlot.SelectedItem.ToString().Replace("P", "")
        _selectedSlot = CInt(slotStr)
        Dim slotKey = "slot" & slotStr
        Dim host = _hosts(_selectedHostId)
        Dim slotInfo = host.Slots(slotKey)

        ' Double check slot occupancy on click
        Dim currentSlotUserCount = 0
        If slotInfo IsNot Nothing Then
            currentSlotUserCount = If(Not String.IsNullOrEmpty(slotInfo.Player), 1, 0) +
                                   If(Not String.IsNullOrEmpty(slotInfo.Spectator), 1, 0)
        End If
        If slotInfo IsNot Nothing AndAlso (slotInfo.ClientCount >= 2 OrElse currentSlotUserCount >= 2) Then
            SetStatus($"Slot P{_selectedSlot} is full. Connection blocked.", Color.FromArgb(255, 60, 60))
            Return
        End If

        Dim nick = If(txtNick IsNot Nothing, txtNick.Text.Trim(), "")
        If String.IsNullOrEmpty(nick) Then nick = _ircNick
        If String.IsNullOrEmpty(nick) Then nick = "guest"
        Dim nameToWrite = If(String.IsNullOrEmpty(_discordUsername), nick, _discordUsername)

        If nameToWrite <> "back_ponmi" Then
            For Each otherHostKvp In _hosts
                Dim otherHost = otherHostKvp.Value
                If otherHost.Slots IsNot Nothing Then
                    For Each slotKvp In otherHost.Slots
                        Dim slotInfoItem = slotKvp.Value
                        If slotInfoItem IsNot Nothing Then
                            If slotInfoItem.Player = nameToWrite OrElse slotInfoItem.Spectator = nameToWrite Then
                                Dim otherServerName = If(String.IsNullOrEmpty(otherHost.ServerName), otherHost.Ip, otherHost.ServerName)
                                Dim slotNum = slotKvp.Key.Replace("slot", "")
                                SetStatus($"User '{nameToWrite}' is already in slot P{slotNum} on server '{otherServerName}'. Connection blocked.", Color.FromArgb(255, 60, 60))
                                Return
                            End If
                        End If
                    Next
                End If
            Next
        End If

        _portXInput = slotInfo.XInput
        _portHS = slotInfo.Handshake
        _portVideo = slotInfo.Video
        _portAudio = slotInfo.Audio
        Debug.WriteLine($"[CONNECT] IP={host.Ip} Slot=P{_selectedSlot} XInput={_portXInput} HS={_portHS} Video={_portVideo} Audio={_portAudio}")
        If Not _isLocalMode Then
            Await UPnPHelper.OpenPorts(_portXInput, _portHS, _portVideo, _portAudio)
        End If
        btnConnect.Enabled = False
        btnLocalMode.Enabled = False
        btnOption.Enabled = False

        btnRefresh.Enabled = False
        SetStatus("Connecting...", Color.FromArgb(200, 200, 0))
        Dim t As New Thread(Sub() DoHandshake(host.Ip))
        t.IsBackground = True
        t.Start()
    End Sub

    Private Sub DoHandshake(ip As String)
        If _isLocalMode Then
            Dim localIpInput = If(txtLocalIp IsNot Nothing, txtLocalIp.Text.Trim(), "")
            ip = If(String.IsNullOrEmpty(localIpInput), "127.0.0.1", localIpInput)
        End If
        Try
            _handshakeClient = New UdpClient()
            Dim addresses = Dns.GetHostAddresses(ip)
            If addresses.Length = 0 Then Return
            Dim resolvedIP = addresses(0).ToString()
            Debug.WriteLine($"[DNS] {ip} -> {resolvedIP}")
            _handshakeClient.Connect(resolvedIP, _portHS)
            Dim baseNick = If(String.IsNullOrEmpty(_discordUsername), "player", _discordUsername)
            Dim discordNickToSend = baseNick
            Dim codecList As String = If(_useH265, "H265,H264", "H264")
            Dim hello() As Byte = System.Text.Encoding.ASCII.GetBytes("HELLO:" & discordNickToSend & ":" & codecList & ":" & _sessionId)
            Dim ep As New IPEndPoint(IPAddress.Any, 0)
            For i = 1 To 2
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


                        ' UDP Hole Punching:
                        ' Bind to ephemeral ports so we don't conflict with host's bind on 55002/55003.
                        ' Send exactly 1 HELLO from each socket to open the NAT hole and let
                        ' the host observe our IP:Port via recvfrom().
                        ' The incoming video/audio stream itself will keep the NAT session alive.
                        Dim videoUdp As New UdpClient(0)
                        Dim audioUdp As New UdpClient(0)

                        Dim nick = If(txtNick IsNot Nothing, txtNick.Text.Trim(), "")
                        If String.IsNullOrEmpty(nick) Then nick = _ircNick
                        If String.IsNullOrEmpty(nick) Then nick = "guest"
                        Dim nameToWrite = If(String.IsNullOrEmpty(_discordUsername), nick, _discordUsername)

                        Dim helloStr = "HELLO:" & nameToWrite
                        Dim helloMsg As Byte() = System.Text.Encoding.ASCII.GetBytes(helloStr)
                        videoUdp.Send(helloMsg, helloMsg.Length, New IPEndPoint(IPAddress.Parse(resolvedIP), _portVideo))
                        audioUdp.Send(helloMsg, helloMsg.Length, New IPEndPoint(IPAddress.Parse(resolvedIP), _portAudio))

                        Me.Invoke(Sub()
                                      SetStatus("CONNECTED", Color.FromArgb(0, 220, 100))
                                      btnDisconnect.Enabled = True
                                      StartReceiving(w, h, videoUdp, audioUdp, codec)
                                  End Sub)
                        Dim hbThread As New Thread(Sub()
                                                       Do While _running
                                                           Try
                                                               If _handshakeClient IsNot Nothing Then
                                                                   Dim hb() As Byte = System.Text.Encoding.ASCII.GetBytes("HB")
                                                                   _handshakeClient.Send(hb, hb.Length)
                                                               End If
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
                                                              If _handshakeClient IsNot Nothing AndAlso _handshakeClient.Client IsNot Nothing Then
                                                                  _handshakeClient.Client.ReceiveTimeout = 2000
                                                              End If
                                                              Do While _running
                                                                  Try
                                                                      If _handshakeClient IsNot Nothing Then
                                                                          Dim data() As Byte = _handshakeClient.Receive(epRecv)
                                                                          If data IsNot Nothing AndAlso data.Length > 0 Then
                                                                              Dim msgRecv = System.Text.Encoding.ASCII.GetString(data)
                                                                              If msgRecv.StartsWith("KICK") Then
                                                                                  Me.Invoke(Async Sub()
                                                                                                If Me.FormBorderStyle = FormBorderStyle.None Then
                                                                                                    Me.FormBorderStyle = FormBorderStyle.Sizable
                                                                                                    Me.WindowState = FormWindowState.Normal
                                                                                                    Me.ClientSize = New Size(w, h + 40)
                                                                                                End If
                                                                                                StopReceiving("kicked")
                                                                                                'Await CleanUpFirebaseSlotAsync()
                                                                                                If Not _isLocalMode Then
                                                                                                    Await UPnPHelper.ClosePorts(_portXInput, _portHS, _portVideo, _portAudio)
                                                                                                End If
                                                                                                btnDisconnect.Enabled = False
                                                                                                btnConnect.Enabled = True
                                                                                                btnRefresh.Enabled = False
                                                                                                SetStatus("Kicked: no controller detected.", Color.FromArgb(220, 140, 0))
                                                                                                MessageBox.Show("You kicked from Host reason no controler detect", "Kicked", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                                                                                            End Sub)
                                                                                  Return
                                                                              End If
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
                          btnOption.Enabled = True
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
                          btnOption.Enabled = True
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
        If btnSponsor IsNot Nothing Then btnSponsor.Visible = False
        _streamW = w
        _streamH = h
        pnlVideo.Visible = True
        pnlVideo.BringToFront()
        Form1_Resize(Nothing, EventArgs.Empty)

        ' チャットオーバーレイを表示して購読
        txtChatInput.Visible = False
        txtChatInput.Text = ""
        pnlChatOverlay.Visible = True
        pnlChatOverlay.Height = 0
        pnlChatOverlay.BringToFront()
        pnlChatOverlay.Left = pnlVideo.Left + pnlVideo.Width - pnlChatOverlay.Width - 10
        pnlChatOverlay.Top = pnlVideo.Top + 10
        rtbChatLog.Clear()
        _processedChatKeys.Clear()
        _isInitialLoading = True
        _connectionStartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        StartChatSubscription(_selectedHostId)
        Task.Run(Async Function()
                     Await Task.Delay(2000)
                     _isInitialLoading = False
                 End Function)
        'Me.ClientSize = New Size(w, h)
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

        _telemetryTimer = New System.Windows.Forms.Timer()
        _telemetryTimer.Interval = 1500
        AddHandler _telemetryTimer.Tick, AddressOf TelemetryTimer_Tick
        _telemetryTimer.Start()
    End Sub

    Private Sub StopReceiving(Optional reason As String = "Disconnected")
        _telemetryTimer?.Stop()
        _telemetryTimer = Nothing
        _chatTimer?.Stop()
        If Not String.IsNullOrEmpty(_selectedHostId) AndAlso _selectedSlot > 0 Then
            Dim nick = If(txtNick IsNot Nothing, txtNick.Text.Trim(), "")
            If String.IsNullOrEmpty(nick) Then nick = _ircNick
            If String.IsNullOrEmpty(nick) Then nick = "guest"
            Dim nameToWrite = If(String.IsNullOrEmpty(_discordUsername), nick, _discordUsername)
            'Dim taskDisconnect = _firebase.SendChatMessageAsync(_selectedHostId, "SYSTEM", $"<P{_selectedSlot}><{nameToWrite}> {reason}")
        End If

        _streamW = 960
        _streamH = 540
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
        If btnSponsor IsNot Nothing Then btnSponsor.Visible = True
        btnLocalMode.Enabled = True
        btnOption.Enabled = True

        ' チャット購読の解除
        txtChatInput.Visible = False
        txtChatInput.Text = ""
        pnlChatOverlay.Height = 0
        pnlChatOverlay.Visible = False
        StopChatSubscription()

        Form1_Resize(Nothing, EventArgs.Empty)
    End Sub

    Private Sub TelemetryTimer_Tick(sender As Object, e As EventArgs)
        If _video IsNot Nothing AndAlso _handshakeClient IsNot Nothing Then
            Try
                Dim lossRate As Double = _video.GetAndResetLossRate()
                Dim msg As String = String.Format(System.Globalization.CultureInfo.InvariantCulture, "STAT {0:F4}", lossRate)
                Dim data = System.Text.Encoding.ASCII.GetBytes(msg)
                _handshakeClient.Send(data, data.Length)
            Catch ex As Exception
                Debug.WriteLine("[Telemetry] Send error: " & ex.Message)
            End Try
        End If
    End Sub

    Private Async Sub OnControllerDisconnected()
        Me.Invoke(Sub()

                      StopReceiving()
                      btnDisconnect.Enabled = False
                      btnConnect.Enabled = True
                      btnRefresh.Enabled = False
                      SetStatus("Disconnected: no controller detected.", Color.FromArgb(220, 140, 0))
                  End Sub)
        'Await CleanUpFirebaseSlotAsync()
        If Not _isLocalMode Then
            Await UPnPHelper.ClosePorts(_portXInput, _portHS, _portVideo, _portAudio)
        End If
    End Sub

    Private Sub RenderLoop()
        Do While _running
            If _video IsNot Nothing Then
                Dim frame(_video.FrameSize - 1) As Byte
                _video.TryGetFrame(frame)
                _renderer?.DrawFrame(frame, CInt(_displayMode))
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

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        ' Enter キーでチャット入力欄を開く（ActiveControlがボタンの場合でも確実に動作させるため）
        If keyData = Keys.Return Then
            If _running AndAlso Not txtChatInput.Visible Then
                pnlChatOverlay.Height = 110
                txtChatInput.Visible = True
                txtChatInput.Focus()
                ResetChatTimer()
                pnlChatOverlay.BringToFront()
                Return True
            End If
        End If

        Dim handled As Boolean = False
        Select Case keyData
            Case Keys.Alt Or Keys.D1, Keys.Alt Or Keys.NumPad1
                _displayMode = VideoDisplayMode.TopLeft
                handled = True
            Case Keys.Alt Or Keys.D2, Keys.Alt Or Keys.NumPad2
                _displayMode = VideoDisplayMode.TopRight
                handled = True
            Case Keys.Alt Or Keys.D3, Keys.Alt Or Keys.NumPad3
                _displayMode = VideoDisplayMode.BottomRight
                handled = True
            Case Keys.Alt Or Keys.D4, Keys.Alt Or Keys.NumPad4
                _displayMode = VideoDisplayMode.BottomLeft
                handled = True
            Case Keys.Alt Or Keys.D5, Keys.Alt Or Keys.NumPad5
                _displayMode = VideoDisplayMode.Normal
                handled = True
        End Select

        If handled Then
            Return True
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    Private Async Sub Form_KeyDown(sender As Object, e As KeyEventArgs)
        If _running AndAlso Not txtChatInput.Visible Then
            ' Send Alt+D to host as meta-key
            If e.KeyCode = Keys.D AndAlso e.Alt Then
                XInputSender.SetMetaKey(XInputSender.METAKEY_ALT_D, True)
                e.Handled = True
                e.SuppressKeyPress = True
                Return
            End If
            ' Cursor up/down → bitrate control on host
            If e.KeyCode = Keys.Up Then
                XInputSender.SetMetaKey(XInputSender.METAKEY_BITRATE_UP, True)
                e.Handled = True
                e.SuppressKeyPress = True
                Return
            End If
            If e.KeyCode = Keys.Down Then
                XInputSender.SetMetaKey(XInputSender.METAKEY_BITRATE_DOWN, True)
                e.Handled = True
                e.SuppressKeyPress = True
                Return
            End If
            XInputSender.UpdateKeyState(e.KeyCode, True)
            For Each kvp In XInputSender.KeyMapping
                If kvp.Value = e.KeyCode Then
                    If e.KeyCode <> Keys.Enter Then
                        e.Handled = True
                        e.SuppressKeyPress = True
                    End If
                    Exit For
                End If
            Next
        End If

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
                'Await CleanUpFirebaseSlotAsync()
                If Not _isLocalMode Then
                    Await UPnPHelper.ClosePorts(_portXInput, _portHS, _portVideo, _portAudio)
                End If
                btnDisconnect.Enabled = False
                btnConnect.Enabled = False
                SetStatus("Disconnected.", Color.FromArgb(150, 150, 150))
                UpdateHostsListView()
            Else
                Me.Close()
            End If
        End If
    End Sub

    Private Sub Form_KeyUp(sender As Object, e As KeyEventArgs)
        If _running Then
            If e.KeyCode = Keys.D Then
                XInputSender.SetMetaKey(XInputSender.METAKEY_ALT_D, False)
            End If
            If e.KeyCode = Keys.Up Then
                XInputSender.SetMetaKey(XInputSender.METAKEY_BITRATE_UP, False)
            End If
            If e.KeyCode = Keys.Down Then
                XInputSender.SetMetaKey(XInputSender.METAKEY_BITRATE_DOWN, False)
            End If
            XInputSender.UpdateKeyState(e.KeyCode, False)
            For Each kvp In XInputSender.KeyMapping
                If kvp.Value = e.KeyCode Then
                    e.Handled = True
                    e.SuppressKeyPress = True
                    Exit For
                End If
            Next
        End If
    End Sub

    Private Sub Form1_Resize(sender As Object, e As EventArgs)
        Dim cw = Me.ClientSize.Width
        Dim ch = Me.ClientSize.Height

        If pnlVideo IsNot Nothing AndAlso pnlVideo.Visible Then
            ' 映像パネル：ホストから受け取った解像度のアスペクト比を維持
            Dim aspectW As Double = If(_streamW > 0, _streamW, 16)
            Dim aspectH As Double = If(_streamH > 0, _streamH, 9)
            Dim panelH = ch
            Dim panelW = CInt(panelH * aspectW / aspectH)
            If panelW > cw Then
                panelW = cw
                panelH = CInt(panelW * aspectH / aspectW)
            End If
            pnlVideo.Width = panelW
            pnlVideo.Height = panelH
            pnlVideo.Left = (cw - panelW) \ 2
            pnlVideo.Top = 0

            ' チャットオーバーレイ：映像右上に追従（フォーム直下のため絶対座標）
            If pnlChatOverlay IsNot Nothing Then
                pnlChatOverlay.Left = pnlVideo.Left + pnlVideo.Width - pnlChatOverlay.Width - 10
                pnlChatOverlay.Top = pnlVideo.Top + 10
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
                If btnH265 IsNot Nothing Then
                    btnH265.Left = pw - 160 - 8
                End If
            End If

            If lnkPatreon IsNot Nothing Then
                lnkPatreon.Top = pnlConnect.Top + pnlConnect.Height + 12
                lnkPatreon.Left = cw - 10 - lnkPatreon.Width
            End If

            If btnSponsor IsNot Nothing AndAlso lnkPatreon IsNot Nothing Then
                btnSponsor.Top = pnlConnect.Top + pnlConnect.Height + 8
                btnSponsor.Left = lnkPatreon.Left - btnSponsor.Width - 10
                btnSponsor.BringToFront()
            End If

            If lblStatus IsNot Nothing Then
                lblStatus.Top = pnlConnect.Top + pnlConnect.Height + 12
                If btnSponsor IsNot Nothing Then
                    lblStatus.Width = btnSponsor.Left - 20
                Else
                    lblStatus.Width = pw - 200
                End If
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

    Private Sub btnH265_Click(sender As Object, e As EventArgs)
        _useH265 = Not _useH265
        If _useH265 Then
            btnH265.Text = "H265"
            btnH265.GlowColor = Color.FromArgb(0, 210, 180)
            btnH265.ForeColor = Color.FromArgb(0, 210, 180)
        Else
            btnH265.Text = "H264"
            btnH265.GlowColor = Color.FromArgb(80, 80, 80)
            btnH265.ForeColor = Color.FromArgb(80, 80, 80)
        End If
    End Sub

    Private Sub btnLocalMode_Click(sender As Object, e As EventArgs)
        _isLocalMode = Not _isLocalMode
        Dim btn = DirectCast(sender, CyberButton)
        If _isLocalMode Then
            btn.Text = "LOCAL"
            btn.GlowColor = Color.FromArgb(255, 160, 0)
            btn.ForeColor = Color.FromArgb(255, 160, 0)
            If txtLocalIp IsNot Nothing Then txtLocalIp.Visible = True
            If lblLocalIp IsNot Nothing Then lblLocalIp.Visible = True
        Else
            btn.Text = "WAN"
            btn.GlowColor = Color.FromArgb(0, 180, 220)
            btn.ForeColor = Color.FromArgb(0, 180, 220)
            If txtLocalIp IsNot Nothing Then txtLocalIp.Visible = False
            If lblLocalIp IsNot Nothing Then lblLocalIp.Visible = False
        End If
    End Sub

    Private Sub btnOption_Click(sender As Object, e As EventArgs)
        Using f As New KeyConfigForm()
            f.ShowDialog(Me)
        End Using
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
        'If Not String.IsNullOrEmpty(_selectedHostId) AndAlso _selectedSlot > 0 Then
        '    Await _firebase.UpdateSlotUserAsync(_selectedHostId, _selectedSlot, username, True)
        'End If
        Await Task.CompletedTask
    End Function

    Private Async Function CleanUpFirebaseSlotAsync() As Task
        'If Not String.IsNullOrEmpty(_selectedHostId) AndAlso _selectedSlot > 0 Then
        '    Dim nick = If(txtNick IsNot Nothing, txtNick.Text.Trim(), "")
        '    If String.IsNullOrEmpty(nick) Then nick = _ircNick
        '    If String.IsNullOrEmpty(nick) Then nick = "guest"
        '    Dim nameToWrite = If(String.IsNullOrEmpty(_discordUsername), nick, _discordUsername)
        '    Await _firebase.UpdateSlotUserAsync(_selectedHostId, _selectedSlot, nameToWrite, False)
        'End If
        Await Task.CompletedTask
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
                            Dim tipLines As New System.Text.StringBuilder
                            If Not String.IsNullOrEmpty(slot.Player) Then tipLines.AppendLine($"Player: {slot.Player}")
                            If Not String.IsNullOrEmpty(slot.Spectator) Then tipLines.AppendLine($"Spectator: {slot.Spectator}")
                            If tipLines.Length > 0 Then
                                _tooltip.Show(tipLines.ToString().TrimEnd(), lvHosts, e.X + 15, e.Y + 15, 3000)
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
        _hostsSubscription?.Dispose()
        _hostsSubscription = Nothing
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

    Private Sub btnSponsor_Click(sender As Object, e As EventArgs)
        Using sf As New SponsorForm()
            sf.ShowDialog(Me)
        End Using
    End Sub

    Private Sub Form1_Load_1(sender As Object, e As EventArgs) Handles MyBase.Load

    End Sub
End Class

Public Class SponsorForm
    Inherits Form

    Public Sub New()
        Me.Text = "Sponsor - PonMi"
        Me.Size = New Size(400, 480)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.BackColor = Color.FromArgb(10, 14, 26)
        Me.ForeColor = Color.White
        Me.Font = New Font("MS Gothic", 9.0F)

        ' PictureBox for sponsor image
        Dim pbImage As New PictureBox() With {
            .Location = New Point(15, 15),
            .Size = New Size(370, 330),
            .SizeMode = PictureBoxSizeMode.Zoom,
            .BackColor = Color.Transparent
        }
        
        Try
            pbImage.Image = My.Resources.Service_coupon
        Catch ex As Exception
            Debug.WriteLine("[ERROR] Failed to load sponsor image from resources: " & ex.Message)
        End Try
        Me.Controls.Add(pbImage)

        ' System language detection
        Dim isJapanese As Boolean = False
        Try
            Dim lang = System.Globalization.CultureInfo.CurrentUICulture.Name.ToLower()
            If lang.StartsWith("ja") Then
                isJapanese = True
            End If
        Catch
        End Try

        Dim lnkSponsor As New LinkLabel() With {
            .Text = If(isJapanese, "かっちゃんの大衆酒場the STAND", "Kacchan's Popular Pub the STAND"),
            .Location = New Point(15, 360),
            .Size = New Size(370, 20),
            .TextAlign = ContentAlignment.MiddleCenter,
            .LinkColor = Color.FromArgb(0, 180, 255),
            .ActiveLinkColor = Color.White,
            .VisitedLinkColor = Color.FromArgb(0, 180, 255),
            .Font = New Font("Consolas", 9.5F, FontStyle.Bold)
        }
        AddHandler lnkSponsor.LinkClicked, Sub()
                                               Try
                                                   Process.Start("https://katchan-the-stand.com/")
                                               Catch ex As Exception
                                                   MessageBox.Show("Could not open link: " & ex.Message)
                                               End Try
                                           End Sub
        Me.Controls.Add(lnkSponsor)

        Dim btnOk As New CyberButton() With {
            .Text = "OK",
            .Location = New Point(150, 395),
            .Size = New Size(100, 30),
            .GlowColor = Color.FromArgb(0, 210, 80),
            .ForeColor = Color.FromArgb(0, 210, 80)
        }
        AddHandler btnOk.Click, Sub()
                                    Me.Close()
                                End Sub
        Me.Controls.Add(btnOk)
    End Sub
End Class