using Microsoft.Extensions.AI;

namespace Shiny.AiConversation.Infrastructure;

public class ManualContextProvider : IContextProvider
{
    readonly Lock sync = new();
    readonly List<string> systemPrompts = [];
    readonly List<AITool> tools = [];

    public void AddSystemPrompt(string prompt)
    {
        lock (this.sync)
            this.systemPrompts.Add(prompt);
    }

    public bool RemoveSystemPrompt(string prompt)
    {
        lock (this.sync)
            return this.systemPrompts.Remove(prompt);
    }

    public void ClearSystemPrompts()
    {
        lock (this.sync)
            this.systemPrompts.Clear();
    }

    public void AddTool(AITool tool)
    {
        lock (this.sync)
            this.tools.Add(tool);
    }

    public bool RemoveTool(AITool tool)
    {
        lock (this.sync)
            return this.tools.Remove(tool);
    }

    public void ClearTools()
    {
        lock (this.sync)
            this.tools.Clear();
    }

    public IEnumerable<string> GetSystemPrompts(AiAcknowledgement acknowledgement)
    {
        lock (this.sync)
            return this.systemPrompts.ToList();
    }

    public IEnumerable<AITool> GetTools()
    {
        lock (this.sync)
            return this.tools.ToList();
    }
}