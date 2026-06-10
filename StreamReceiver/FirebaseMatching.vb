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

Imports Firebase.Auth
Imports Firebase.Auth.Providers
Imports Firebase.Database
Imports Firebase.Database.Query
Imports Newtonsoft.Json

Public Class SlotInfo
    <JsonProperty("xinput")>
    Public Property XInput As Integer

    <JsonProperty("video")>
    Public Property Video As Integer

    <JsonProperty("audio")>
    Public Property Audio As Integer

    <JsonProperty("available")>
    Public Property Available As Boolean

    <JsonProperty("user")>
    Public Property User As String
End Class

Public Class HostInfo
    <JsonProperty("timestamp")>
    Public Property Timestamp As Long

    <JsonProperty("ip")>
    Public Property Ip As String

    <JsonProperty("gametitle")>
    Public Property GameTitle As String

    <JsonProperty("slot1")>
    Public Property Slot1 As SlotInfo

    <JsonProperty("slot2")>
    Public Property Slot2 As SlotInfo

    <JsonProperty("slot3")>
    Public Property Slot3 As SlotInfo

    <JsonProperty("slot4")>
    Public Property Slot4 As SlotInfo

    ' 既存コードとの互換性のためSlots辞書を動的生成
    Public ReadOnly Property Slots As Dictionary(Of String, SlotInfo)
        Get
            Dim d As New Dictionary(Of String, SlotInfo)
            If Slot1 IsNot Nothing Then d("slot1") = Slot1
            If Slot2 IsNot Nothing Then d("slot2") = Slot2
            If Slot3 IsNot Nothing Then d("slot3") = Slot3
            If Slot4 IsNot Nothing Then d("slot4") = Slot4
            Return d
        End Get
    End Property
End Class

Public Class FirebaseMatchingClient
    Private Const ApiKey As String = "YourApiKey"
    Private Const DbUrl As String = "https://YourDbUrl.firebasedatabase.app"
    Private Const AuthDomain As String = "YourAuthDomain"
    Private Const TimeoutMinutes As Integer = 360

    Private dbClient As FirebaseClient
    Private authClient As FirebaseAuthClient

    ' -------------------------------------------------------
    ' 初期化（匿名認証）
    ' -------------------------------------------------------
    Public Async Function InitializeAsync() As Task
        Try
            Dim config = New FirebaseAuthConfig() With {
                .ApiKey = ApiKey,
                .AuthDomain = AuthDomain,
                .Providers = New FirebaseAuthProvider() {}
            }
            authClient = New FirebaseAuthClient(config)
            Dim userCredential = Await authClient.SignInAnonymouslyAsync()

            dbClient = New FirebaseClient(DbUrl, New FirebaseOptions With {
                .AuthTokenAsyncFactory = Async Function() Await userCredential.User.GetIdTokenAsync()
            })

            Debug.WriteLine("[Firebase] ログイン成功 UID: " & userCredential.User.Uid)
        Catch ex As Exception
            Debug.WriteLine("[ERROR] Firebase初期化失敗: " & ex.Message)
        End Try
    End Function

    ' -------------------------------------------------------
    ' ホスト一覧取得（タイムアウト除外済み）
    ' -------------------------------------------------------
    Public Async Function GetActiveHostsAsync() As Task(Of Dictionary(Of String, HostInfo))
        Dim result = New Dictionary(Of String, HostInfo)
        Try
            Dim hosts = Await dbClient.Child("hosts").OnceAsync(Of HostInfo)()
            Dim nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            Dim timeoutMs = TimeoutMinutes * 60 * 1000L

            For Each host In hosts
                If host.Object IsNot Nothing Then
                    Debug.WriteLine($"[Firebase] ホスト={host.Key} timestamp={host.Object.Timestamp} diff={nowUnix - host.Object.Timestamp}ms")
                    Dim elapsed = nowUnix - host.Object.Timestamp
                    If elapsed <= timeoutMs Then
                        result(host.Key) = host.Object
                    Else
                        Debug.WriteLine($"[Firebase] タイムアウト除外: {host.Key}")
                    End If
                End If
            Next
        Catch ex As Exception
            Debug.WriteLine("[ERROR] ホスト一覧取得失敗: " & ex.Message)
        End Try
        Return result
    End Function

    ' -------------------------------------------------------
    ' スロットの接続ユーザー情報の更新/クリア
    ' -------------------------------------------------------
    Public Async Function UpdateSlotUserAsync(hostId As String, slotIndex As Integer, username As String) As Task
        Try
            Await dbClient.Child("hosts").Child(hostId).Child("slot" & slotIndex.ToString()).Child("user").PutAsync(Of String)(username)
        Catch ex As Exception
            Debug.WriteLine("[ERROR] UpdateSlotUserAsync: " & ex.Message)
        End Try
    End Function

End Class