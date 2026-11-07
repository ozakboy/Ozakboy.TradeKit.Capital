using System.Diagnostics;

namespace Ozakboy.TradeKit.Capital.Host.Tests;

/// <summary>
/// 宿主壽命的兩道保險:父行程看門狗與結束保險(實機發現父行程結束後宿主仍在跑之後補上)。
/// The host's two lifetime safety nets, added after a host was found running long after its parent ended.
/// </summary>
[TestClass]
public sealed class ServeLifetimeTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [TestMethod]
    public void TheWatchFiresOnceWhenTheParentIsGone()
    {
        bool alive = true;
        int fired = 0;
        using ParentWatch watch = new(() => Volatile.Read(ref alive), TimeSpan.FromMilliseconds(20), () => Interlocked.Increment(ref fired), TimeProvider.System);

        Thread.Sleep(100);
        Assert.AreEqual(0, Volatile.Read(ref fired), "nothing while the parent lives");

        Volatile.Write(ref alive, false);

        Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref fired) == 1, Wait));
        Thread.Sleep(100);
        Assert.AreEqual(1, Volatile.Read(ref fired), "only once");
    }

    [TestMethod]
    public void StartWithoutAPidWatchesNothing()
    {
        using StringWriter stderr = new();
        HostLog log = new(stderr, TimeProvider.System);

        using ParentWatch? watch = ParentWatch.Start(null, TimeSpan.FromMilliseconds(20), () => Assert.Fail("must not fire"), log, TimeProvider.System);

        Assert.IsNull(watch);
        StringAssert.Contains(stderr.ToString(), "CAPITAL_PARENT_PID");
    }

    [TestMethod]
    public void StartWithALivingParentDoesNotFire()
    {
        using StringWriter stderr = new();
        HostLog log = new(stderr, TimeProvider.System);
        int fired = 0;

        using ParentWatch? watch = ParentWatch.Start(Environment.ProcessId, TimeSpan.FromMilliseconds(20), () => Interlocked.Increment(ref fired), log, TimeProvider.System);
        Thread.Sleep(150);

        Assert.IsNotNull(watch);
        Assert.AreEqual(0, Volatile.Read(ref fired));
    }

    [TestMethod]
    public void StartWithAParentThatAlreadyExitedFiresAtOnce()
    {
        using Process shortLived = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false, CreateNoWindow = true })!;
        shortLived.WaitForExit();
        int pid = shortLived.Id;
        using StringWriter stderr = new();
        HostLog log = new(stderr, TimeProvider.System);
        int fired = 0;

        using ParentWatch? watch = ParentWatch.Start(pid, TimeSpan.FromMilliseconds(20), () => Interlocked.Increment(ref fired), log, TimeProvider.System);

        // 已結束的行程:要嘛當下就拿不到(立刻觸發),要嘛拿到已結束的行程物件(下一次檢查觸發)。
        // An ended process is either not found at all (fires at once) or found as exited (fires on the next check).
        Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref fired) == 1, Wait));
    }

    [TestMethod]
    public void ReadParentPidAcceptsOnlyAPositiveInteger()
    {
        string? before = Environment.GetEnvironmentVariable(ServeEnvironmentNames.ParentPid);

        try
        {
            Environment.SetEnvironmentVariable(ServeEnvironmentNames.ParentPid, "4242");
            Assert.AreEqual(4242, ParentWatch.ReadParentPid());
            Environment.SetEnvironmentVariable(ServeEnvironmentNames.ParentPid, "-1");
            Assert.IsNull(ParentWatch.ReadParentPid());
            Environment.SetEnvironmentVariable(ServeEnvironmentNames.ParentPid, "abc");
            Assert.IsNull(ParentWatch.ReadParentPid());
            Environment.SetEnvironmentVariable(ServeEnvironmentNames.ParentPid, null);
            Assert.IsNull(ParentWatch.ReadParentPid());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServeEnvironmentNames.ParentPid, before);
        }
    }

    [TestMethod]
    public void TheExitGuardForcesTheExitAfterTheGraceWithTheFirstCode()
    {
        using StringWriter stderr = new();
        HostLog log = new(TextWriter.Synchronized(stderr), TimeProvider.System);
        int exitCode = -1;
        int calls = 0;
        using ExitGuard guard = new(TimeSpan.FromMilliseconds(100), code =>
        {
            Volatile.Write(ref exitCode, code);
            Interlocked.Increment(ref calls);
        }, log, TimeProvider.System);

        guard.Arm("stdin 已關閉", 0);
        guard.Arm("協定層已結束", 1);

        Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref calls) == 1, Wait), "forced after the grace");
        Assert.AreEqual(0, Volatile.Read(ref exitCode), "the first arm wins");
        StringAssert.Contains(stderr.ToString(), "強制結束");
    }

    [TestMethod]
    public void TheExitGuardDoesNothingUntilArmed()
    {
        using StringWriter stderr = new();
        HostLog log = new(stderr, TimeProvider.System);
        int calls = 0;
        using ExitGuard guard = new(TimeSpan.FromMilliseconds(20), _ => Interlocked.Increment(ref calls), log, TimeProvider.System);

        Thread.Sleep(150);

        Assert.AreEqual(0, Volatile.Read(ref calls));
    }
}
