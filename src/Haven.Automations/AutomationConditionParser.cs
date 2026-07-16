namespace Haven.Automations;

public static class AutomationConditionParser
{
    public static AutomationConditionResult Parse(string? response) =>
        AutomationRunner.ParseConditionResult(response);
}
