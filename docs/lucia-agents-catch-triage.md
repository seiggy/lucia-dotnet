# lucia.Agents catch triage

Generated from a scan of `catch (Exception)` sites (Mongo files excluded, they are being deleted). Classification is heuristic, from the catch body. Verify before acting.

## Result

| Class | Count | Verdict |
|---|---|---|
| log-and-continue | 63 | Review each. Narrow the type, or let it propagate if the caller can act on it. |
| tool-boundary (returns text) | 29 | Keep at the boundary. Centralise in a `ToolRunner` helper and stop returning `ex.Message` to the LLM. |
| swallow-return-default | 17 | Review each. Returning a default hides failure; log at least, narrow where possible. |
| rethrow | 7 | Fine. |
| silent | 2 | Fix first. Empty or unlogged catch. |

Cancellation. 101 of 118 sites could swallow OperationCanceledException around awaited work. Fixed in this branch by adding `when (ex is not OperationCanceledException)` to each. 14 sites had no awaits nearby, 3 already filtered.

## Sites to fix first (silent and swallow-return-default)

| File | Line | Class |
|---|---|---|
| GitHubCopilot\CopilotConnectService.cs | 62 | swallow-return-default |
| Orchestration\LocalAgentInvoker.cs | 161 | swallow-return-default |
| Orchestration\RemoteAgentInvoker.cs | 110 | swallow-return-default |
| PluginFramework\PluginLoader.cs | 83 | swallow-return-default |
| PluginFramework\PluginScriptHost.cs | 69 | swallow-return-default |
| Providers\ModelProviderResolver.cs | 100 | swallow-return-default |
| Providers\ModelProviderResolver.cs | 116 | swallow-return-default |
| Services\EntityLocationService.cs | 714 | swallow-return-default |
| Services\EntityLocationService.cs | 980 | swallow-return-default |
| Services\EntityLocationService.cs | 1293 | swallow-return-default |
| Services\InputRequiredTimeoutService.cs | 151 | silent |
| Services\RedisPromptCacheService.cs | 231 | swallow-return-default |
| Services\RedisPromptCacheService.cs | 433 | swallow-return-default |
| Services\RedisPromptCacheService.cs | 538 | swallow-return-default |
| Services\RedisPromptCacheService.cs | 588 | swallow-return-default |
| Services\RedisPromptCacheService.cs | 643 | swallow-return-default |
| Services\RedisPromptCacheService.cs | 693 | swallow-return-default |
| Services\RedisPromptCacheService.cs | 750 | swallow-return-default |
| Training\AgentTracingChatClient.cs | 176 | silent |

## Sites by file

| File | Catches |
|---|---|
| Services\RedisPromptCacheService.cs | 13 |
| Skills\ClimateControlSkill.cs | 12 |
| Skills\FanControlSkill.cs | 9 |
| Services\EntityLocationService.cs | 8 |
| Skills\SensorControlSkill.cs | 6 |
| Skills\SecurityControlSkill.cs | 5 |
| Mcp\McpToolRegistry.cs | 5 |
| Skills\ListSkill.cs | 5 |
| Orchestration\WorkflowFactory.cs | 4 |
| Services\AgentInitializationService.cs | 4 |
| Orchestration\RouterExecutor.cs | 3 |
| Services\DynamicAgentLoader.cs | 3 |
| Orchestration\LuciaEngine.cs | 3 |
| Skills\SceneControlSkill.cs | 3 |
| Orchestration\AgentDispatchExecutor.cs | 2 |
| Integration\PromptCachingChatClient.cs | 2 |
| PluginFramework\GitPluginRepositorySource.cs | 2 |
| Services\TaskArchivalService.cs | 2 |
| GitHubCopilot\CopilotClientLifecycleService.cs | 2 |
| Training\TraceCaptureObserver.cs | 2 |
| Services\InputRequiredTimeoutService.cs | 2 |
| Training\AgentTracingChatClient.cs | 2 |
| Providers\ModelProviderResolver.cs | 2 |
| Skills\LightControlSkill.cs | 2 |
| Agents\DynamicAgent.cs | 1 |
| PluginFramework\PluginManagementService.cs | 1 |
| PluginFramework\PluginScriptHost.cs | 1 |
| PluginFramework\PluginLoader.cs | 1 |
| PluginFramework\LocalPluginRepositorySource.cs | 1 |
| Orchestration\SessionManager.cs | 1 |
| Orchestration\ResultAggregatorExecutor.cs | 1 |
| Orchestration\RemoteAgentInvoker.cs | 1 |
| Orchestration\LocalAgentInvoker.cs | 1 |
| Integration\ChatClientResolver.cs | 1 |
| GitHubCopilot\CopilotConnectService.cs | 1 |
| DataStores\ArchivingTaskStore.cs | 1 |
| Configuration\ConfigSeeder.cs | 1 |
| Providers\EmbeddingProviderResolver.cs | 1 |
| Training\TraceRetentionService.cs | 1 |


