using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Pomodoro.ViewModels;
using Joktan.Pomodoro.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Pomodoro.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public class PomodoroViewTests : DataFolderTest
{
    [Fact]
    public Task Shows_the_timer_and_uses_switches_for_its_settings() => Headless.Run(() =>
    {
        var vm = new PomodoroViewModel(new PomodoroSettings());
        var window = Headless.Show(new PomodoroView(), vm);

        Assert.Equal("25:00", Headless.Find<TextBlock>(window, t => t.Name == "RemainingText").Text);
        Assert.Equal(3, Headless.All<ToggleSwitch>(window).Count());
        Assert.Empty(Headless.All<CheckBox>(window));
        Assert.Equal(25m, Headless.All<NumericUpDown>(window).First().Value);
        window.Close();
    });

    [Fact]
    public Task A_length_typed_on_the_page_reaches_the_timer() => Headless.Run(() =>
    {
        var vm = new PomodoroViewModel(new PomodoroSettings());
        var window = Headless.Show(new PomodoroView(), vm);

        Headless.All<NumericUpDown>(window).First().Value = 40;
        Headless.Settle();

        Assert.Equal(40, vm.FocusMinutes);
        Assert.Equal("40:00", Headless.Find<TextBlock>(window, t => t.Name == "RemainingText").Text);
        window.Close();
    });

    [Fact]
    public Task The_main_button_follows_the_timer() => Headless.Run(() =>
    {
        var vm = new PomodoroViewModel(new PomodoroSettings());
        var window = Headless.Show(new PomodoroView(), vm);
        var button = Headless.Find<Button>(window, b => b.Name == "StartPauseButton");
        Assert.Equal("Start", button.Content);

        button.Command!.Execute(null);
        Headless.Settle();
        Assert.Equal("Pause", button.Content);

        vm.ResetCommand.Execute(null);
        window.Close();
    });
}
