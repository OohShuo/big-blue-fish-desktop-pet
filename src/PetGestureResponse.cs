namespace BigBlueFish;

internal sealed record PetGestureResponse(string? Action, string Text)
{
    internal static PetGestureResponse For(PetGesture kind) => kind switch
    {
        PetGesture.Shake => new("点击回应-傲娇生气", "主人！鱼鱼要晕啦！"),
        PetGesture.HeadPet => new("点击回应-开心跃动", "嘿嘿，最喜欢主人摸摸头啦～"),
        PetGesture.PokedSurprised => new("点击回应-害羞惊讶", "呜哇！主人怎么一直戳鱼鱼呀……"),
        PetGesture.PokedAngry => new("点击回应-傲娇生气", "再戳鱼鱼，鱼鱼可要生气啦！"),
        PetGesture.LongPress => new("点击回应-害羞惊讶", "主人怎么一直盯着鱼鱼呀……"),
        PetGesture.HeldAloft => new(null, "主人，可以放鱼鱼下来了吗？"),
        PetGesture.TailTickle => new("点击回应-挠痒咯咯笑", "哈哈～尾巴不可以挠啦！"),
        PetGesture.Startled => new("被吓一跳", "呜哇！主人吓到鱼鱼啦！"),
        PetGesture.Soothe => new("点击回应-元气挥手", "哼哼～这次就原谅主人啦。"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
