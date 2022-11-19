using System.Collections.Concurrent;

namespace RCaron.FunLibrary;

public static class MotorPool
{
    public static List<MotorPoolItem> Pool = new();

    public static MotorPoolItem GetAndPrepare(Motor parent)
    {
        void Prepare(Motor motor)
        {
            motor.Lines = parent.Lines;
            motor.Modules = parent.Modules;
            motor.MainFileScope = parent.MainFileScope;
            motor.GlobalScope.SetVariable("parentMotor", parent);
        }
        Motor res;
        for (var i = 0; i < Pool.Count; i++)
        {
            if (Pool[i].InUse == false)
            {
                Pool[i].InUse = true;
                return Pool[i];
            }
        }

        res = new Motor(new RCaronRunnerContext(parent.MainFileScope));
        Prepare(res);
        var item = new MotorPoolItem(res) { InUse = true };
        Pool.Add(item);
        return item;
    }
}

/// <summary>
/// Not actually disposable but it's a good way to make sure the motor is returned to the pool
/// </summary>
/// <param name="Motor"></param>
/// <param name="InUse"></param>
public record MotorPoolItem(Motor Motor) : IDisposable
{
    public bool InUse { get; set; }
    public void Dispose()
    {
        this.InUse = false;
    }
}