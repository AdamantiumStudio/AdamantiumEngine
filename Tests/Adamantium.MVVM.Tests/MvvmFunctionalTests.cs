using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.Core.Commands;
using NUnit.Framework;

namespace Adamantium.MVVM.Tests;

[TestFixture]
public class MvvmFunctionalTests
{
    [Test]
    public void Bindable_RaisesPropertyChanged_AndCallsHook()
    {
        var vm = new PersonViewModel();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.FirstName = "Alice";

        Assert.That(vm.FirstName, Is.EqualTo("Alice"));
        Assert.That(raised, Does.Contain("FirstName"));
        Assert.That(vm.LastChangedProperty, Is.EqualTo("FirstName"));   // OnFirstNameChanged hook fired
    }

    [Test]
    public void Bindable_SameValue_DoesNotNotify()
    {
        var vm = new PersonViewModel { FirstName = "A" };
        var count = 0;
        vm.PropertyChanged += (_, _) => count++;

        vm.FirstName = "A";

        Assert.That(count, Is.Zero);
    }

    [Test]
    public void Affects_NotifiesDependentProperty()
    {
        var vm = new PersonViewModel();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.LastName = "Smith";

        Assert.That(raised, Does.Contain("LastName"));
        Assert.That(raised, Does.Contain("FullName"));
    }

    [Test]
    public void Command_RespectsCanExecute()
    {
        var vm = new PersonViewModel();

        Assert.That(vm.SaveCommand.CanExecute(), Is.False);   // FirstName empty
        vm.SaveCommand.Execute();
        Assert.That(vm.SaveCount, Is.Zero);

        vm.FirstName = "A";
        Assert.That(vm.SaveCommand.CanExecute(), Is.True);
        vm.SaveCommand.Execute();
        Assert.That(vm.SaveCount, Is.EqualTo(1));
    }

    [Test]
    public void Affects_Command_RaisesCanExecuteChanged()
    {
        var vm = new PersonViewModel();
        var command = vm.SaveCommand;   // realize the lazy command so its backing field exists
        var raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;

        vm.FirstName = "A";             // [Affects(nameof(SaveCommand))] -> RaiseCanExecuteChanged

        Assert.That(raised, Is.GreaterThan(0));
    }

    [Test]
    public void Command_IsCachedInstance()
    {
        var vm = new PersonViewModel();
        Assert.That(vm.SaveCommand, Is.SameAs(vm.SaveCommand));   // lazy, single instance
    }

    [Test]
    public async Task AsyncCommand_RunsAndReportsIsRunning()
    {
        var vm = new PersonViewModel();
        var command = vm.LoadCommand;   // typed as AdamantiumAsyncCommand → IsRunning/ExecuteAsync/Cancel

        Assert.That(command.CanExecute(), Is.True);
        Assert.That(command.IsRunning, Is.False);

        var task = command.ExecuteAsync();          // runs to the first await, then yields (gate not released)
        Assert.That(command.IsRunning, Is.True);
        Assert.That(command.CanExecute(), Is.False);   // disable-while-running

        vm.LoadGate.SetResult(true);                // release the gate
        await task;

        Assert.That(command.IsRunning, Is.False);
        Assert.That(command.CanExecute(), Is.True);
        Assert.That(vm.LoadCount, Is.EqualTo(1));
    }

    [Test]
    public async Task AsyncCommand_Cancel_StopsAndSwallows()
    {
        var vm = new PersonViewModel();
        var command = vm.LoadCommand;

        var task = command.ExecuteAsync();
        Assert.That(command.IsRunning, Is.True);

        command.Cancel();
        await task;                                 // cancellation is swallowed → no throw

        Assert.That(command.IsRunning, Is.False);
        Assert.That(vm.LoadCount, Is.Zero);         // canceled before the increment
    }

    [Test]
    public async Task AsyncCommand_BlocksReentryWhileRunning()
    {
        var vm = new PersonViewModel();
        var command = vm.LoadCommand;

        var first = command.ExecuteAsync();
        await command.ExecuteAsync();               // CanExecute is false → returns immediately, no second run

        vm.LoadGate.SetResult(true);
        await first;

        Assert.That(vm.LoadCount, Is.EqualTo(1));    // ran exactly once
    }

    [Test]
    public async Task AsyncCommandAllowingConcurrentRuns_RunsEachPressWhileRunning()
    {
        var vm = new StepsViewModel();
        var command = vm.TakeCommand;

        var first = command.ExecuteAsync();
        Assert.That(command.IsRunning, Is.True);
        Assert.That(command.CanExecute(), Is.True);

        var second = command.ExecuteAsync();
        Assert.That(vm.Gates, Has.Count.EqualTo(2));

        vm.Gates[0].SetResult(true);
        await first;
        Assert.That(command.IsRunning, Is.True, "the second run is still under way");

        vm.Gates[1].SetResult(true);
        await second;
        Assert.That(command.IsRunning, Is.False);
        Assert.That(vm.Taken, Is.EqualTo(2));
    }

    [Test]
    public async Task AsyncCommandAllowingConcurrentRuns_TellsRunningOnceForAllRuns()
    {
        var vm = new StepsViewModel();
        var command = vm.TakeCommand;
        var told = 0;
        command.CanExecuteChanged += (_, _) => told++;

        var first = command.ExecuteAsync();
        var second = command.ExecuteAsync();
        vm.Gates[1].SetResult(true);
        await second;
        vm.Gates[0].SetResult(true);
        await first;

        Assert.That(told, Is.EqualTo(2), "once when the first run starts, once when the last one ends");
    }

    [Test]
    public async Task AsyncCommandAllowingConcurrentRuns_CancelStopsEveryRun()
    {
        var vm = new StepsViewModel();
        var command = vm.TakeCommand;

        var first = command.ExecuteAsync();
        var second = command.ExecuteAsync();
        command.Cancel();
        await Task.WhenAll(first, second);

        Assert.That(command.IsRunning, Is.False);
        Assert.That(vm.Taken, Is.Zero);
    }

    [Test]
    public async Task TypedAsyncCommandAllowingConcurrentRuns_RunsEachPressWhileRunning()
    {
        var vm = new StepsViewModel();
        var command = vm.NameCommand;

        var first = command.ExecuteAsync("one");
        Assert.That(command.CanExecute("two"), Is.True);
        var second = command.ExecuteAsync("two");

        vm.Gates[0].SetResult(true);
        vm.Gates[1].SetResult(true);
        await Task.WhenAll(first, second);

        Assert.That(vm.Named, Is.EqualTo(new[] { "one", "two" }));
    }

    [Test]
    public void AllowingConcurrentRuns_LeavesAVoidCommandAsItIs()
    {
        var vm = new StepsViewModel();

        vm.CountCommand.Execute();

        Assert.That(vm.Taken, Is.EqualTo(1));
    }

    [Test]
    public void TypedCommand_PassesParameter()
    {
        var vm = new PersonViewModel();

        vm.ApplyCommand.Execute("Neo");                  // typed call (AdamantiumCommand<string>)
        Assert.That(vm.AppliedName, Is.EqualTo("Neo"));

        ((ICommand)vm.ApplyCommand).Execute("Trinity");  // object path — how the UI/binding invokes it
        Assert.That(vm.AppliedName, Is.EqualTo("Trinity"));
    }

    [Test]
    public async Task TypedAsyncCommand_PassesParameterAndRuns()
    {
        var vm = new PersonViewModel();
        var command = vm.ApplyAsyncCommand;              // AdamantiumAsyncCommand<string>

        var task = command.ExecuteAsync("Morpheus");
        Assert.That(command.IsRunning, Is.True);

        vm.ApplyGate.SetResult(true);
        await task;

        Assert.That(command.IsRunning, Is.False);
        Assert.That(vm.LoadedName, Is.EqualTo("Morpheus"));
    }

    [Test]
    public void PartialProperty_NotifiesAndCallsHook()
    {
        var vm = new PartialPropertyViewModel();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Title = "Hello";

        Assert.That(vm.Title, Is.EqualTo("Hello"));
        Assert.That(raised, Does.Contain("Title"));
        Assert.That(vm.LastChanged, Is.EqualTo("Hello"));   // OnTitleChanged hook fired

        raised.Clear();
        vm.Title = "Hello";                                  // same value → no notify
        Assert.That(raised, Is.Empty);
    }

    [Test]
    public void PartialProperty_KeepsTheAccessItWasDeclaredWith()
    {
        var vm = new PartialPropertyViewModel();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.CountUp();
        vm.Classify("Box");
        vm.Note = "Kept";
        vm.Label = "Named";
        var made = new PartialPropertyViewModel { Origin = "Made" };

        var count = typeof(PartialPropertyViewModel).GetProperty(nameof(PartialPropertyViewModel.Count));
        var kind = typeof(PartialPropertyViewModel).GetProperty(nameof(PartialPropertyViewModel.Kind));
        var note = typeof(PartialPropertyViewModel).GetProperty(nameof(PartialPropertyViewModel.Note),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.Multiple(() =>
        {
            Assert.That(raised, Is.EqualTo(new[] { "Count", "Kind", "Note", "Label" }));
            Assert.That(made.Origin, Is.EqualTo("Made"));
            Assert.That(typeof(PartialPropertyViewModel).GetProperty("Label").GetMethod.IsVirtual, Is.True);
            Assert.That(PropertyAccessors.Find(typeof(PartialPropertyViewModel), "Origin").CanWrite, Is.True);
            Assert.That(count.SetMethod.IsPrivate, Is.True);
            Assert.That(kind.SetMethod.IsFamily, Is.True);
            Assert.That(note.GetMethod.IsAssembly, Is.True);
            Assert.That(PropertyAccessors.Find(typeof(PartialPropertyViewModel), "Count").CanWrite, Is.False);
            Assert.That(PropertyAccessors.Find(typeof(PartialPropertyViewModel), "Kind").CanWrite, Is.False);
        });
    }

    [Test]
    public void PartialProperty_NotifiesWhatItAffects()
    {
        var vm = new PartialPropertyViewModel();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Subtitle = "Below";

        Assert.That(raised, Is.EqualTo(new[] { "Subtitle", "Caption" }));
    }

    [Test]
    public void OverridableHooks_ReachTheDerivedClass()
    {
        var vm = new DerivedHooksViewModel();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Name = "Neo";
        vm.Count = 3;

        Assert.That(vm.Changing, Is.EqualTo("Neo"));
        Assert.That(vm.Changed, Is.EqualTo("Neo"));
        Assert.That(vm.CountChanged, Is.EqualTo(3));
        List<string> expected = ["Name", "Count"];
        Assert.That(raised, Is.EqualTo(expected));
    }

    [Test]
    public void Validation_SurfacesErrorsViaINotifyDataErrorInfo()
    {
        var vm = new RegistrationViewModel();
        Assert.That(vm.HasErrors, Is.False);          // nothing validated yet

        vm.Age = 200;                                  // out of [Range(0,120)]
        Assert.That(vm.HasErrors, Is.True);
        Assert.That(vm.GetErrors(nameof(vm.Age)).Cast<string>().Any(), Is.True);

        vm.Age = 30;                                   // back in range → error cleared
        Assert.That(vm.GetErrors(nameof(vm.Age)).Cast<string>().Any(), Is.False);
        Assert.That(vm.HasErrors, Is.False);
    }

    [Test]
    public void Validation_RaisesErrorsChanged()
    {
        var vm = new RegistrationViewModel();
        var changed = new List<string>();
        vm.ErrorsChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Name = "";                                  // [Required] fails on empty
        Assert.That(changed, Does.Contain(nameof(vm.Name)));
        Assert.That(vm.GetErrors(nameof(vm.Name)).Cast<string>().Any(), Is.True);

        vm.Name = "Bob";                               // valid → cleared
        Assert.That(vm.GetErrors(nameof(vm.Name)).Cast<string>().Any(), Is.False);
    }

    [Test]
    public void ViewModelAttribute_InjectsInpc()
    {
        var vm = new StandaloneViewModel();
        Assert.That(vm, Is.InstanceOf<INotifyPropertyChanged>());

        var raised = new List<string>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.Counter = 5;

        Assert.That(vm.Counter, Is.EqualTo(5));
        Assert.That(raised, Does.Contain("Counter"));
    }
}
