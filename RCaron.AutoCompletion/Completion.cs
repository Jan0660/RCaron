namespace RCaron.AutoCompletion;

public record Completion(CompletionThing Thing, (int Start, int End) Position);