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
Imports System.IO.Compression
Imports System.Net

Public Module FfmpegHelper

    Private ReadOnly FfmpegDir As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg")
    Private ReadOnly FfmpegExePath As String = Path.Combine(FfmpegDir, "ffmpeg.exe")

    Private Const DownloadUrl As String =
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"

    ''' <summary>
    ''' ffmpeg.exe のフルパスを返す。
    ''' アプリ実行フォルダ → ffmpeg サブフォルダ → PATH の順で探す。
    ''' </summary>
    Public Function GetFfmpegPath() As String
        ' 1. アプリ直下
        Dim localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe")
        If File.Exists(localPath) Then Return localPath

        ' 2. ffmpeg サブフォルダ
        If File.Exists(FfmpegExePath) Then Return FfmpegExePath

        ' 3. PATH 上にあるか
        Dim pathDirs = Environment.GetEnvironmentVariable("PATH")
        If pathDirs IsNot Nothing Then
            For Each dirr In pathDirs.Split(";"c)
                Dim p = Path.Combine(dirr.Trim(), "ffmpeg.exe")
                If File.Exists(p) Then Return p
            Next
        End If

        Return Nothing
    End Function

    ''' <summary>
    ''' ffmpeg が見つからない場合にダウンロードする。
    ''' IProgress(Of String) でステータスメッセージを返す。
    ''' </summary>
    Public Async Function EnsureFfmpegAsync(Optional progress As IProgress(Of String) = Nothing) As Task(Of Boolean)
        ' 既にある
        If GetFfmpegPath() IsNot Nothing Then
            Return True
        End If

        progress?.Report("ffmpeg をダウンロードしています...")

        Dim zipPath = Path.Combine(Path.GetTempPath(), "ffmpeg-essentials.zip")

        Try
            ' ダウンロード
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12

            Using client As New WebClient()
                ' プログレス表示
                AddHandler client.DownloadProgressChanged,
                    Sub(s, e)
                        progress?.Report($"ffmpeg ダウンロード中... {e.ProgressPercentage}%")
                    End Sub

                Await client.DownloadFileTaskAsync(New Uri(DownloadUrl), zipPath)
            End Using

            progress?.Report("ffmpeg を展開しています...")

            ' 展開先を作成
            If Not Directory.Exists(FfmpegDir) Then
                Directory.CreateDirectory(FfmpegDir)
            End If

            ' ZIP内の ffmpeg.exe を探して展開
            Using archive = ZipFile.OpenRead(zipPath)
                For Each entry In archive.Entries
                    If entry.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) Then
                        entry.ExtractToFile(FfmpegExePath, True)
                        Exit For
                    End If
                Next
            End Using

            ' ZIPを削除
            Try : File.Delete(zipPath) : Catch : End Try

            If File.Exists(FfmpegExePath) Then
                progress?.Report("ffmpeg の準備が完了しました")
                Return True
            Else
                progress?.Report("ffmpeg.exe が ZIP 内に見つかりませんでした")
                Return False
            End If

        Catch ex As Exception
            progress?.Report($"ffmpeg ダウンロードエラー: {ex.Message}")
            Try : File.Delete(zipPath) : Catch : End Try
            Return False
        End Try
    End Function

End Module
