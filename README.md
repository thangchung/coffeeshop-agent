# coffeeshop-agent
.NET Agents technologies

## MCP

```
npx @modelcontextprotocol/inspector
```

```
http://localhost:5001/mcp/sse
```

## A2A

```sh
$Env:GEMINI_API_KEY = '<key>'; agentgateway -f .\agentgateway\config.yaml
```

```
http://localhost:5000/.well-known/agent.json
```

## TODO

- [ ] Semantic Caching with Semantic Kernel: https://share.google/aimode/gxqpWpfekktrOidbr
- [ ] Microsoft.Extensions.AI + Ollama: https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai#tool-calling

## References

- https://github.com/thangchung/practical-dotnet-aspire
- https://github.com/thangchung/mcp-labs/blob/feat/a2a_mcp_auth/a2a_mcp_auth_dotnet
- https://devblogs.microsoft.com/foundry/building-ai-agents-a2a-dotnet-sdk/
