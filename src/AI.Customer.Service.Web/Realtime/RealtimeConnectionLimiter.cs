namespace AI.Customer.Service.Web.Realtime;

/// <summary>单节点最多 64 个实时连接，未认证连接也计入额度，防止闲置耗尽资源。</summary>
public sealed class RealtimeConnectionLimiter : IDisposable
{
    /// <summary>节点固定连接上限，与 Java 当前约定一致。</summary>
    public SemaphoreSlim Slots { get; } = new(64, 64);

    /// <summary>应用停止时释放信号量。</summary>
    public void Dispose() => Slots.Dispose();
}
