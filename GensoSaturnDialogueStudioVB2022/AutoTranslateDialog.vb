Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.Windows.Forms

Public Class AutoTranslateDialog
    Inherits Form

    Private ReadOnly backend As New ComboBox()
    Private ReadOnly model As New TextBox()
    Private ReadOnly host As New TextBox()
    Private ReadOnly contact As New TextBox()

    Public Property Configuration As New AutoTranslateSettings()

    Public Sub New()
        Text = "Translate All • Japanese to English"
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False : MaximizeBox = False : ShowInTaskbar = False
        Width = 645 : Height = 402
        Font = New Font("Segoe UI", 10.0F)
        BackColor = Color.FromArgb(24, 33, 48)
        ForeColor = Color.FromArgb(230, 238, 249)

        Dim top As New Label With {
            .Text = "TRANSLATE ALL — KEEP ORIGINAL GAME LIMITS",
            .Font = New Font("Segoe UI Semibold", 15.0F, FontStyle.Bold),
            .ForeColor = Color.FromArgb(61, 189, 218),
            .Dock = DockStyle.Top, .Height = 56, .Padding = New Padding(18, 13, 0, 0)}
        Controls.Add(top)

        Dim fields As New TableLayoutPanel With {.Dock = DockStyle.Top, .Height = 188,
            .Padding = New Padding(18, 4, 18, 4), .ColumnCount = 2, .RowCount = 4}
        fields.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150))
        fields.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For i = 0 To 3
            fields.RowStyles.Add(New RowStyle(SizeType.Absolute, 44))
        Next
        backend.DropDownStyle = ComboBoxStyle.DropDownList
        backend.Items.AddRange(New String() {"Local Ollama", "MyMemory (free online)"})
        backend.SelectedIndex = 0
        model.Text = "qwen2.5:7b"
        host.Text = "http://127.0.0.1:11434"
        contact.PlaceholderText = "Optional; only used for MyMemory quota"
        Dim labels = {"Translation engine", "Local model name", "Local model endpoint", "MyMemory contact email"}
        Dim items As Control() = {backend, model, host, contact}
        For i = 0 To 3
            Dim l As New Label With {.Text = labels(i), .Dock = DockStyle.Fill,
                .TextAlign = ContentAlignment.MiddleLeft, .ForeColor = Color.FromArgb(180, 200, 218)}
            fields.Controls.Add(l, 0, i)
            items(i).Dock = DockStyle.Fill
            items(i).BackColor = Color.FromArgb(39, 51, 70)
            items(i).ForeColor = Color.White
            fields.Controls.Add(items(i), 1, i)
        Next
        Controls.Add(fields)
        Dim description As New Label With {.Dock = DockStyle.Top, .Height = 81,
            .Text = "Local Ollama runs on your PC. Install Ollama and pull qwen2.5:7b before starting. " &
                    "MyMemory sends Japanese text to an online provider and has daily character limits. " &
                    "Translations are drafts; original bytes, Japanese, pointers and control tags remain unchanged.",
            .Padding = New Padding(20, 8, 20, 3), .ForeColor = Color.FromArgb(170, 189, 209)}
        Controls.Add(description)
        Dim foot As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .Height = 55,
            .FlowDirection = FlowDirection.RightToLeft, .Padding = New Padding(14, 6, 14, 5)}
        Dim run As New Button With {.Text = "TRANSLATE MISSING", .Width = 190, .Height = 35,
            .BackColor = Color.FromArgb(61, 189, 218), .ForeColor = Color.FromArgb(14, 20, 32),
            .FlatStyle = FlatStyle.Flat, .Font = New Font("Segoe UI Semibold", 9.0F, FontStyle.Bold)}
        run.FlatAppearance.BorderSize = 0
        Dim cancel As New Button With {.Text = "CANCEL", .Width = 116, .Height = 35,
            .BackColor = Color.FromArgb(48, 63, 83), .ForeColor = Color.White, .FlatStyle = FlatStyle.Flat}
        cancel.FlatAppearance.BorderSize = 0
        AddHandler run.Click, AddressOf AcceptConfiguration
        AddHandler cancel.Click, Sub()
                                     DialogResult = DialogResult.Cancel
                                     Close()
                                 End Sub
        foot.Controls.Add(run) : foot.Controls.Add(cancel)
        Controls.Add(foot)
        AddHandler backend.SelectedIndexChanged, Sub()
                                                     Dim local = backend.SelectedIndex = 0
                                                     model.Enabled = local
                                                     host.Enabled = local
                                                     contact.Enabled = Not local
                                                 End Sub
        contact.Enabled = False
    End Sub

    Private Sub AcceptConfiguration(sender As Object, e As EventArgs)
        Dim selectedProvider = backend.SelectedItem.ToString()
        If selectedProvider = "MyMemory (free online)" AndAlso
            MessageBox.Show(Me, "MyMemory receives your Japanese dialogue text. Continue?", "Online privacy",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
        If selectedProvider = "Local Ollama" AndAlso String.IsNullOrWhiteSpace(model.Text) Then
            MessageBox.Show(Me, "Enter the name of an Ollama model installed on your PC.", "Missing local model")
            Return
        End If
        Configuration = New AutoTranslateSettings With {.Backend = selectedProvider,
            .Model = model.Text.Trim(), .Endpoint = host.Text.Trim(),
            .ContactEmail = contact.Text.Trim()}
        DialogResult = DialogResult.OK
        Close()
    End Sub
End Class
