using Demo.Shared;

ApplicationConfiguration.Initialize();

using var form = new Form
{
    Text = "Demo.WinForms",
    Width = 420,
    Height = 220,
    StartPosition = FormStartPosition.CenterScreen
};

form.Controls.Add(new Label
{
    AutoSize = true,
    Left = 24,
    Top = 32,
    Text = DemoCatalog.Current.DisplayName
});

Application.Run(form);
