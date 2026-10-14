using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

// QYL1002's three GenAI rows: each library is used here and never opted in, so the generator reports
// one warning per row. Nothing calls these methods; they only have to compile.
internal static class GenAiOptInsProbe
{
    internal static object Chat(IChatClient client) => client.GetResponseAsync("probe");

    internal static object Agent(AIAgent agent) => agent.RunAsync("probe");

    internal static object Workflow(WorkflowBuilder builder) => builder.Build();
}
