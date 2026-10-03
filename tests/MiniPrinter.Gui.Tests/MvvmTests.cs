using System.ComponentModel;

namespace MiniPrinter.Gui.Tests;

public class MvvmTests
{
    private sealed class Sample : ObservableObject
    {
        private int _value;
        public int Value { get => _value; set => Set(ref _value, value); }
    }

    [Fact]
    public void A_changed_property_raises_the_event_once_and_an_equal_value_raises_nothing()
    {
        var sample = new Sample();
        var names = new List<string?>();
        ((INotifyPropertyChanged)sample).PropertyChanged += (_, e) => names.Add(e.PropertyName);
        sample.Value = 3;
        sample.Value = 3;
        sample.Value = 4;
        Assert.Equal(["Value", "Value"], names);
    }

    [Fact]
    public void A_relay_command_runs_its_action_and_follows_its_condition()
    {
        var allowed = false;
        var runs = 0;
        var command = new RelayCommand(() => runs++, () => allowed);
        var changes = 0;
        command.CanExecuteChanged += (_, _) => changes++;
        Assert.False(command.CanExecute(null));
        allowed = true;
        command.RaiseCanExecuteChanged();
        Assert.True(command.CanExecute(null));
        command.Execute(null);
        Assert.Equal((1, 1), (runs, changes));
    }

    [Fact]
    public async Task An_async_command_cannot_be_started_again_while_it_runs()
    {
        var gate = new TaskCompletionSource();
        var starts = 0;
        var command = new AsyncCommand(async () => { starts++; await gate.Task; });
        var changes = 0;
        command.CanExecuteChanged += (_, _) => changes++;

        var first = command.ExecuteAsync();
        Assert.True(command.IsRunning);
        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync();           // ignored: already running
        Assert.Equal(1, starts);

        gate.SetResult();
        await first;
        Assert.False(command.IsRunning);
        Assert.True(command.CanExecute(null));
        Assert.Equal(2, changes);               // disabled, then enabled again
    }

    [Fact]
    public async Task A_failure_is_reported_to_the_handler_and_the_command_works_again()
    {
        var command = new AsyncCommand(() => throw new InvalidOperationException("boom"));
        Exception? seen = null;
        command.Failed += ex => seen = ex;
        await command.ExecuteAsync();
        Assert.IsType<InvalidOperationException>(seen);
        Assert.False(command.IsRunning);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task Without_a_failure_handler_the_exception_reaches_the_caller()
    {
        var command = new AsyncCommand(() => throw new InvalidOperationException("boom"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync());
        Assert.False(command.IsRunning);
    }

    [Fact]
    public void The_immediate_dispatcher_runs_the_action_where_it_is_called()
    {
        var ran = false;
        ImmediateDispatcher.Instance.Post(() => ran = true);
        Assert.True(ran);
    }
}
