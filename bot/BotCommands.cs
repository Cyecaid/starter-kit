namespace bot;

public record Move(V Destination) : BotCommand;
public record Wait : BotCommand;
public record Use(V Target) : BotCommand;