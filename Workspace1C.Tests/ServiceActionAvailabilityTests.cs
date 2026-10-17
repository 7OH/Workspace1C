using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Исчерпывающая таблица доступности команд служб (issue #11, раздел 3.1):
/// каждый статус × каждая операция, влияние CanPauseAndContinue/CanStop,
/// платформенное правило «Перезапустить остановленной» (Linux: systemctl restart).
/// </summary>
public sealed class ServiceActionAvailabilityTests
{
    private const ServicePlatform Windows = ServicePlatform.Windows;
    private const ServicePlatform Linux = ServicePlatform.Linux;

    // ===================== Windows: базовая таблица =====================

    [Theory]
    [InlineData(ServiceStatus.Stopped, true, true, ServiceAction.Start)]
    public void Windows_Stopped_OnlyStartIsAllowed(ServiceStatus status, bool canPause, bool canStop, ServiceAction allowed)
    {
        foreach (var action in AllActions())
        {
            var expected = action == allowed;
            Assert.Equal(expected, ServiceActionAvailability.CanExecute(action, status, canPause, canStop, Windows));
        }
    }

    [Theory]
    [InlineData(true, ServiceAction.Stop, ServiceAction.Pause, ServiceAction.Restart)]
    [InlineData(false, ServiceAction.Stop, ServiceAction.Restart)]
    public void Windows_Running_ActionsDependOnCanPauseAndContinue(bool canPause, params ServiceAction[] allowed)
    {
        foreach (var action in AllActions())
        {
            var expected = allowed.Contains(action);
            Assert.Equal(expected, ServiceActionAvailability.CanExecute(action, ServiceStatus.Running, canPause, canStop: true, Windows));
        }
    }

    [Theory]
    [InlineData(ServiceAction.Stop, ServiceAction.Continue)]
    public void Windows_Paused_StopAndContinueAllowed(params ServiceAction[] allowed)
    {
        foreach (var action in AllActions())
        {
            var expected = allowed.Contains(action);
            Assert.Equal(expected, ServiceActionAvailability.CanExecute(action, ServiceStatus.Paused, canPauseAndContinue: true, canStop: true, Windows));
        }
    }

    [Theory]
    [InlineData(ServiceStatus.Starting)]
    [InlineData(ServiceStatus.Stopping)]
    public void Windows_TransitionStates_NothingAllowed(ServiceStatus status)
    {
        foreach (var action in AllActions())
        {
            Assert.False(ServiceActionAvailability.CanExecute(action, status, canPauseAndContinue: true, canStop: true, Windows));
        }
    }

    [Theory]
    [InlineData(ServiceStatus.Unknown)]
    [InlineData(ServiceStatus.NotFound)]
    public void Windows_UnknownOrNotFound_NothingAllowed(ServiceStatus status)
    {
        foreach (var action in AllActions())
        {
            Assert.False(ServiceActionAvailability.CanExecute(action, status, canPauseAndContinue: false, canStop: false, Windows));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Windows_StopRequiresCanStop(bool canStop, bool expected)
    {
        Assert.Equal(expected,
            ServiceActionAvailability.CanExecute(ServiceAction.Stop, ServiceStatus.Running, canPauseAndContinue: true, canStop, Windows));
    }

    // ===================== Linux: своя мапа =====================

    [Fact]
    public void Linux_Stopped_RestartAllowed()
    {
        Assert.True(ServiceActionAvailability.CanExecute(ServiceAction.Restart, ServiceStatus.Stopped, canPauseAndContinue: false, canStop: true, Linux));
    }

    [Fact]
    public void Linux_Stopped_StartAllowedRestartAllowedButNoPause()
    {
        var actions = AllActions().Where(a =>
            a is ServiceAction.Start or ServiceAction.Restart).ToArray();
        foreach (var action in AllActions())
        {
            var expected = actions.Contains(action);
            Assert.Equal(expected,
                ServiceActionAvailability.CanExecute(action, ServiceStatus.Stopped, canPauseAndContinue: false, canStop: true, Linux));
        }
    }

    [Fact]
    public void Linux_Running_PauseAndContinueNeverAllowed()
    {
        // systemd не поддерживает pause/continue для обычных служб: CanPauseAndContinue=false
        // у всех служб, поэтому Pause/Continue недоступны и при Running.
        Assert.False(ServiceActionAvailability.CanExecute(ServiceAction.Pause, ServiceStatus.Running, canPauseAndContinue: false, canStop: true, Linux));
        Assert.False(ServiceActionAvailability.CanExecute(ServiceAction.Continue, ServiceStatus.Paused, canPauseAndContinue: false, canStop: true, Linux));
        Assert.True(ServiceActionAvailability.CanExecute(ServiceAction.Stop, ServiceStatus.Running, canPauseAndContinue: false, canStop: true, Linux));
        Assert.True(ServiceActionAvailability.CanExecute(ServiceAction.Restart, ServiceStatus.Running, canPauseAndContinue: false, canStop: true, Linux));
    }

    // ===================== CanExecuteAny («Все сервисы») =====================

    [Fact]
    public void CanExecuteAny_ReturnsTrueWhenAtLeastOneApplicable()
    {
        var services = new[]
        {
            new ServiceInfo { ServiceName = "run", Status = ServiceStatus.Running, CanStop = true, CanPauseAndContinue = true }
        };

        Assert.True(ServiceActionAvailability.CanExecuteAny(services, ServiceAction.Stop, Windows));
        Assert.True(ServiceActionAvailability.CanExecuteAny(services, ServiceAction.Restart, Windows));
        // В наборе только запущенная служба: «Запустить» неприменим.
        Assert.False(ServiceActionAvailability.CanExecuteAny(services, ServiceAction.Start, Windows));
    }

    [Fact]
    public void CanExecuteAny_StartIsApplicableWhenStoppedPresent()
    {
        var services = new[]
        {
            new ServiceInfo { ServiceName = "run", Status = ServiceStatus.Running, CanStop = true },
            new ServiceInfo { ServiceName = "stopped", Status = ServiceStatus.Stopped, CanStop = true }
        };

        Assert.True(ServiceActionAvailability.CanExecuteAny(services, ServiceAction.Start, Windows));
    }

    [Fact]
    public void CanExecuteAny_Empty_ReturnsFalse()
    {
        Assert.False(ServiceActionAvailability.CanExecuteAny(Array.Empty<ServiceInfo>(), ServiceAction.Start, Windows));
        Assert.False(ServiceActionAvailability.CanExecuteAny(null!, ServiceAction.Start, Windows));
    }

    private static IEnumerable<ServiceAction> AllActions() =>
        Enum.GetValues<ServiceAction>();
}