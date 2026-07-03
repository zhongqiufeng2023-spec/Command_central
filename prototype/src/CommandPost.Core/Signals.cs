namespace CommandPost.Core;

/// <summary>信号通道:旌旗(视觉,需通视)/ 金鼓(听觉,需在声程)。低带宽、近即时、会被敌方读到。</summary>
public enum SignalKind { Flag, Drum }

/// <summary>信号词表(低带宽,只能传简单令):进 / 守 / 退 / 集结。</summary>
public enum SignalCode { Advance, Halt, Retreat, Rally }

/// <summary>一次待解算的旗鼓信号(中军帐发出)。TargetUnitId=null 表示全军(广播)。</summary>
public readonly record struct PendingSignal(SignalKind Kind, SignalCode Code, int? TargetUnitId);

public static class SignalNames
{
    public static string Cn(this SignalKind k) => k == SignalKind.Flag ? "旌旗" : "金鼓";

    public static string Cn(this SignalCode c) => c switch
    {
        SignalCode.Advance => "进",
        SignalCode.Halt => "守",
        SignalCode.Retreat => "退",
        _ => "集结"
    };
}
