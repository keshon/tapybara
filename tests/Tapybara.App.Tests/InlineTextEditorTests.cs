using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Tapybara.App.Controls;
using Xunit;

namespace Tapybara.App.Tests;

/// <summary>Нативная привязка и повторное использование поля без запуска приложения и звуковых устройств.</summary>
public sealed class InlineTextEditorTests
{
    private sealed record Row(string Text);

    private static void OnSta(Action check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { check(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void Begin(InlineTextEditor editor) => editor.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, editor)
    {
        RoutedEvent = Keyboard.GotKeyboardFocusEvent,
    });

    [Fact]
    public void TypingCommitsOneLocalTransactionAndKeepsNativeBinding()
    {
        OnSta(() =>
        {
            var row = new Row("recognized");
            var editor = new InlineTextEditor { DataContext = row };
            editor.SetBinding(TextBox.TextProperty, new Binding(nameof(Row.Text)) { Mode = BindingMode.OneWay });
            int commits = 0;
            editor.CommitRequested += (_, context, text) =>
            {
                commits++;
                Assert.Same(row, context);
                Assert.Equal("corrected text", text);
                return true;
            };
            Begin(editor);
            editor.SelectAll();
            editor.SelectedText = "corrected";
            editor.AppendText(" text");
            editor.Flush();
            editor.Flush();
            Assert.Equal(1, commits);
            Assert.True(BindingOperations.IsDataBound(editor, TextBox.TextProperty));
        });
    }

    [Fact]
    public void RecyclingCommitsOldDraftAgainstOldRowInsteadOfNewText()
    {
        OnSta(() =>
        {
            var first = new Row("first");
            var second = new Row("second");
            var editor = new InlineTextEditor { DataContext = first };
            editor.SetBinding(TextBox.TextProperty, new Binding(nameof(Row.Text)) { Mode = BindingMode.OneWay });
            var commits = new List<(object? Context, string Text)>();
            editor.CommitRequested += (_, context, text) => { commits.Add((context, text)); return true; };
            Begin(editor);
            editor.SelectAll();
            editor.SelectedText = "first corrected";
            editor.DataContext = second;
            editor.Flush();
            Assert.Single(commits);
            Assert.Same(first, commits[0].Context);
            Assert.Equal("first corrected", commits[0].Text);
            Assert.Equal("second", editor.Text);
        });
    }

    [Fact]
    public void EscapeRestoresDraftWithoutSendingAnEdit()
    {
        OnSta(() =>
        {
            var editor = new InlineTextEditor { Text = "original" };
            int commits = 0;
            editor.CommitRequested += (_, _, _) => { commits++; return true; };
            Begin(editor);
            editor.SelectAll();
            editor.SelectedText = "mistake";
            using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("test") { Width = 0, Height = 0 });
            editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            });
            editor.Flush();
            Assert.Equal("original", editor.Text);
            Assert.Equal(0, commits);
        });
    }

    [Fact]
    public void RejectedCommitRetainsDraftForExplicitRetry()
    {
        OnSta(() =>
        {
            var row = new Row("original");
            var editor = new InlineTextEditor { DataContext = row, Text = row.Text };
            bool accept = false;
            int attempts = 0;
            editor.CommitRequested += (_, context, text) =>
            {
                attempts++;
                Assert.Same(row, context);
                Assert.Equal("draft", text);
                return accept;
            };
            Begin(editor);
            editor.SelectAll();
            editor.SelectedText = "draft";
            Assert.False(editor.Flush());
            Assert.True(editor.HasDraftChanges);
            Assert.Equal("draft", editor.Text);
            accept = true;
            Assert.True(editor.Flush());
            Assert.False(editor.HasDraftChanges);
            Assert.Equal(2, attempts);
        });
    }

    [Fact]
    public void RejectedRowCannotBeBypassedByFocusingAnotherRowThenLeavingCall()
    {
        OnSta(() =>
        {
            var first = new InlineTextEditor { Text = "original" };
            var second = new InlineTextEditor { Text = "another row" };
            var scope = new InlineEditScope();
            bool accept = false;
            first.CommitRequested += (_, _, _) => accept;
            Assert.True(scope.TryActivate(first));
            Begin(first);
            first.SelectAll();
            first.SelectedText = "draft";
            Assert.False(first.Flush());
            Assert.False(scope.TryActivate(second));
            Assert.Same(first, scope.Active);
            Assert.False(scope.TryLeave());
            Assert.False(scope.TryClear());
            Assert.True(first.HasDraftChanges);
            accept = true;
            Assert.True(scope.TryActivate(second));
            Assert.True(scope.TryLeave());
        });
    }
}
