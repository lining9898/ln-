using System.Windows;
using AiKnowledgeAssistant.Desktop.ViewModels;

namespace AiKnowledgeAssistant.Desktop.UI;

public partial class KnowledgeBaseNameDialog : Window
{
    private readonly Action<string> save;
    public KnowledgeBaseNameDialog(string title, string initialName, Action<string> save)
    {
        InitializeComponent();
        Title = title;
        this.save = save;
        NameInput.Text = initialName;
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            save(NameInput.Text);
            DialogResult = true;
        }
        catch (Exception exception) when (KnowledgeBasesViewModel.IsExpectedError(exception))
        {
            ErrorText.Text = KnowledgeBasesViewModel.ErrorMessage(exception);
            NameInput.Focus();
        }
    }
}
