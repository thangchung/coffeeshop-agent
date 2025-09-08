# coffeeshop-agent
.NET Agents technologies

## High level architecture

```mermaid
flowchart LR
  %% Top-level orchestration
  subgraph AppHost["AppHost (Aspire)"]
    AH1[Wires env & connection strings]
  end

  %% Identity
  subgraph AAD["Azure AD (Microsoft.Identity.Web)"]
    AAD1[Authorization/Token Issuer]
  end

  %% LLM
  subgraph LLM["LLM Provider"]
    OAI[Azure OpenAI Chat Model]
  end

  %% Counter Service
  subgraph Counter["CounterService"]
    C_TM[TaskManager]
    C_AG[CounterAgent]
    C_SK[Semantic Kernel\nChatCompletionAgent]
    C_MCP[MCP Client - SSE]
  end

  %% Product Catalog Service
  subgraph Product["ProductCatalogService"]
    P_MCP[MCP Server /mcp]
    P_TOOLS[McpTools + McpResources]
  end

  %% Barista Service
  subgraph Barista["BaristaService"]
    B_TM[TaskManager]
    B_AG[BaristaAgent]
  end

  %% Kitchen Service
  subgraph Kitchen["KitchenService"]
    K_TM[TaskManager]
    K_AG[KitchenAgent]
  end

  %% Client entry
  Client[Client/UI or External Agent]

  %% AppHost wiring
  AH1 --> Counter
  AH1 --> Barista
  AH1 --> Kitchen
  AH1 --> Product
  AH1 -. connection strings .-> C_SK
  AH1 -. env (AzureAd:*) .-> AAD1

  %% Attach patterns inside services
  C_TM <-. Attach .-> C_AG
  B_TM <-. Attach .-> B_AG
  K_TM <-. Attach .-> K_AG
  P_MCP --> P_TOOLS

  %% Entry into Counter (A2A)
  Client -->|A2A JSON-RPC over HTTP\nscope: CoffeeShop.Counter.ReadWrite| C_TM

  %% Counter auth on-behalf-of
  C_AG -->|TokenAcquisition - OBO| AAD1

  %% Counter -> Product via MCP
  C_AG -->|MCP SSE /mcp\nBearer: CoffeeShop.Mcp.Product.ReadWrite| P_MCP
  C_MCP --- C_AG

  %% Counter -> LLM for classification
  C_SK -->|ChatCompletion| OAI
  C_AG --- C_SK

  %% Counter -> Barista/Kitchen via A2A
  C_AG -->|A2A SendMessage\nBearer: CoffeeShop.Barista.ReadWrite| B_TM
  C_AG -->|A2A SendMessage\nBearer: CoffeeShop.Kitchen.ReadWrite| K_TM

  %% Barista/Kitchen internals
  B_AG --> B_TM
  K_AG --> K_TM
```

## MCP

```
npx @modelcontextprotocol/inspector
```

```
http://localhost:5001/mcp/sse
```

```
http://localhost:5001/.well-known/oauth-protected-resource
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
- https://devblogs.microsoft.com/dotnet/build-a-model-context-protocol-mcp-server-in-csharp/
