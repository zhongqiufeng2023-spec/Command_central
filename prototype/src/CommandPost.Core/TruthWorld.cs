namespace CommandPost.Core;

/// <summary>
/// 真相世界:后端持续 tick 推进的真实模拟状态。玩家永不可直接看见。
/// 唯一的「事实来源」。
/// </summary>
public sealed class TruthWorld
{
    public int Tick { get; set; }
    public TerrainGrid Terrain { get; init; } = default!;
    public List<Unit> Units { get; } = new();
    public List<Messenger> Messengers { get; } = new();
    public List<Scout> Scouts { get; } = new();

    /// <summary>各方中军帐 / 大本营位置(战报送达此处)。</summary>
    public Vec2 FriendHq { get; init; }
    public Vec2 EnemyHq { get; init; }

    public Vec2 HqOf(Side s) => s == Side.Friend ? FriendHq : EnemyHq;

    public IEnumerable<Unit> Living => Units.Where(u => u.Alive);
    public IEnumerable<Unit> LivingOf(Side s) => Units.Where(u => u.Alive && u.Side == s);

    public Unit? UnitById(int id) => Units.FirstOrDefault(u => u.Id == id);
}
