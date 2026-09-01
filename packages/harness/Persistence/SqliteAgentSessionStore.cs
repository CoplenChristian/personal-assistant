using System.Text.Json;
using Microsoft.Data.Sqlite;
using PersonalAssistant.Harness.Activity;
using PersonalAssistant.Harness.Agents;

namespace PersonalAssistant.Harness.Persistence;

public sealed class SqliteAgentSessionStore : IAgentSessionStore
{
    private readonly SqliteHarnessDatabase database;

    public SqliteAgentSessionStore(SqliteHarnessDatabase database)
    {
        this.database = database;
    }

    public AgentStatus EnsureAgent(AgentDefinition definition)
    {
        database.ExecuteInTransaction(transaction =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            var existingAgent = FindAgent(transaction, definition.Id);
            if (existingAgent is null)
            {
                using var insertAgent = database.Connection.CreateCommand();
                insertAgent.Transaction = transaction;
                insertAgent.CommandText = """
                    INSERT INTO agents
                        (id, name, runtime, working_directory, realms_json, skills_json, browser_profile, memory_scope, scheduled_task_permissions_json, auto_start, desired_state, created_at, updated_at)
                    VALUES
                        ($id, $name, $runtime, $working_directory, $realms_json, $skills_json, $browser_profile, $memory_scope, $scheduled_task_permissions_json, $auto_start, $desired_state, $created_at, $updated_at);
                    """;
                AddDefinitionParameters(insertAgent, definition);
                insertAgent.Parameters.AddWithValue("$desired_state", ToDatabaseValue(InitialDesiredState(definition)));
                insertAgent.Parameters.AddWithValue("$created_at", now);
                insertAgent.Parameters.AddWithValue("$updated_at", now);
                insertAgent.ExecuteNonQuery();
            }
            else
            {
                using var updateAgent = database.Connection.CreateCommand();
                updateAgent.Transaction = transaction;
                updateAgent.CommandText = """
                    UPDATE agents
                    SET name = $name,
                        runtime = $runtime,
                        working_directory = $working_directory,
                        realms_json = $realms_json,
                        skills_json = $skills_json,
                        browser_profile = $browser_profile,
                        memory_scope = $memory_scope,
                        scheduled_task_permissions_json = $scheduled_task_permissions_json,
                        auto_start = $auto_start,
                        updated_at = $updated_at
                    WHERE id = $id;
                    """;
                AddDefinitionParameters(updateAgent, definition);
                updateAgent.Parameters.AddWithValue("$updated_at", now);
                updateAgent.ExecuteNonQuery();
            }

            var existingSessionName = FindSessionName(transaction, definition.Id);
            if (existingSessionName is null)
            {
                using var insertSession = database.Connection.CreateCommand();
                insertSession.Transaction = transaction;
                insertSession.CommandText = """
                    INSERT INTO sessions
                        (id, agent_id, tmux_session_name, runtime, observed_state)
                    VALUES
                        ($id, $agent_id, $tmux_session_name, $runtime, $observed_state);
                    """;
                insertSession.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                insertSession.Parameters.AddWithValue("$agent_id", definition.Id);
                insertSession.Parameters.AddWithValue("$tmux_session_name", definition.TmuxSessionName);
                insertSession.Parameters.AddWithValue("$runtime", definition.Runtime);
                insertSession.Parameters.AddWithValue("$observed_state", ToDatabaseValue(SessionObservedState.Missing));
                insertSession.ExecuteNonQuery();
            }
            else if (!string.Equals(existingSessionName, definition.TmuxSessionName, StringComparison.Ordinal))
            {
                throw new AgentConfigurationException("The persisted tmux session name does not match the current bootstrap prefix.");
            }
        });

        return ReadStatus(definition);
    }

    public AgentStatus ReadStatus(AgentDefinition definition)
    {
        lock (database.SyncRoot)
        {
            var status = ReadStatusLocked(definition);
            return status ?? throw new AgentConfigurationException("The personal agent is not registered.");
        }
    }

    public void SetDesiredState(string agentId, AgentDesiredState desiredState)
    {
        database.ExecuteInTransaction(transaction =>
        {
            using var command = database.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE agents
                SET desired_state = $desired_state, updated_at = $updated_at
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$desired_state", ToDatabaseValue(desiredState));
            command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$id", agentId);
            if (command.ExecuteNonQuery() != 1)
            {
                throw new AgentConfigurationException($"The agent {agentId} is not registered.");
            }
        });
    }

    public AgentStatus RecordObservation(
        AgentDefinition definition,
        SessionObservedState observedState,
        string? lastError,
        ActivityEvent? activityEvent,
        string? nativeConversationReference = null,
        AgentDesiredState? desiredState = null)
    {
        database.ExecuteInTransaction(transaction =>
        {
            using var agentCommand = database.Connection.CreateCommand();
            agentCommand.Transaction = transaction;
            agentCommand.CommandText = """
                UPDATE agents
                SET desired_state = COALESCE($desired_state, desired_state),
                    updated_at = $now
                WHERE id = $agent_id;
                """;
            agentCommand.Parameters.AddWithValue("$desired_state", desiredState is null ? DBNull.Value : ToDatabaseValue(desiredState.Value));
            agentCommand.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            agentCommand.Parameters.AddWithValue("$agent_id", definition.Id);
            if (agentCommand.ExecuteNonQuery() != 1)
            {
                throw new AgentConfigurationException("The personal agent is not registered.");
            }

            using var command = database.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE sessions
                SET observed_state = $observed_state,
                    native_conversation_ref = COALESCE($native_conversation_ref, native_conversation_ref),
                    started_at = CASE
                        WHEN $observed_state = 'starting' THEN $now
                        ELSE started_at
                    END,
                    last_seen_at = CASE
                        WHEN $observed_state IN ('starting', 'running') THEN $now
                        ELSE last_seen_at
                    END,
                    stopped_at = CASE
                        WHEN $observed_state IN ('exited', 'error')
                            AND $desired_state = 'stopped' THEN $now
                        ELSE stopped_at
                    END,
                    last_error = $last_error
                WHERE agent_id = $agent_id;
                """;
            command.Parameters.AddWithValue("$observed_state", ToDatabaseValue(observedState));
            command.Parameters.AddWithValue("$desired_state", desiredState is null ? DBNull.Value : ToDatabaseValue(desiredState.Value));
            command.Parameters.AddWithValue("$native_conversation_ref", (object?)nativeConversationReference ?? DBNull.Value);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$last_error", (object?)lastError ?? DBNull.Value);
            command.Parameters.AddWithValue("$agent_id", definition.Id);
            if (command.ExecuteNonQuery() != 1)
            {
                throw new AgentConfigurationException("The personal agent session is not registered.");
            }

            if (activityEvent is not null)
            {
                database.InsertActivityEvent(transaction, activityEvent);
            }
        });

        return ReadStatus(definition);
    }

    public void RecordConversationReference(string agentId, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 512)
        {
            throw new AgentConfigurationException("The native conversation reference is invalid.");
        }

        database.ExecuteInTransaction(transaction =>
        {
            using var command = database.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE sessions SET native_conversation_ref = $reference WHERE agent_id = $agent_id;";
            command.Parameters.AddWithValue("$reference", reference);
            command.Parameters.AddWithValue("$agent_id", agentId);
            if (command.ExecuteNonQuery() != 1)
            {
                throw new AgentConfigurationException("The personal agent session is not registered.");
            }
        });
    }

    private AgentStatus? ReadStatusLocked(AgentDefinition definition)
    {
        using var command = database.Connection.CreateCommand();
        command.CommandText = """
            SELECT a.desired_state,
                   s.id,
                   s.agent_id,
                   s.tmux_session_name,
                   s.runtime,
                   s.native_conversation_ref,
                   s.observed_state,
                   s.started_at,
                   s.last_seen_at,
                   s.stopped_at,
                   s.last_error
            FROM agents a
            INNER JOIN sessions s ON s.agent_id = a.id
            WHERE a.id = $agent_id;
            """;
        command.Parameters.AddWithValue("$agent_id", definition.Id);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var session = new PersistedSession(
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            ParseObservedState(reader.GetString(6)),
            ReadDate(reader, 7),
            ReadDate(reader, 8),
            ReadDate(reader, 9),
            reader.IsDBNull(10) ? null : reader.GetString(10));
        return new AgentStatus(definition, ParseDesiredState(reader.GetString(0)), session, false, false);
    }

    private string? FindAgent(SqliteTransaction transaction, string agentId)
    {
        using var command = database.Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM agents WHERE id = $id;";
        command.Parameters.AddWithValue("$id", agentId);
        return command.ExecuteScalar() as string;
    }

    private string? FindSessionName(SqliteTransaction transaction, string agentId)
    {
        using var command = database.Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT tmux_session_name FROM sessions WHERE agent_id = $id;";
        command.Parameters.AddWithValue("$id", agentId);
        return command.ExecuteScalar() as string;
    }

    private static void AddDefinitionParameters(SqliteCommand command, AgentDefinition definition)
    {
        command.Parameters.AddWithValue("$id", definition.Id);
        command.Parameters.AddWithValue("$name", definition.Name);
        command.Parameters.AddWithValue("$runtime", definition.Runtime);
        command.Parameters.AddWithValue("$working_directory", definition.WorkingDirectory);
        command.Parameters.AddWithValue("$realms_json", JsonSerializer.Serialize(definition.Realms));
        command.Parameters.AddWithValue("$skills_json", JsonSerializer.Serialize(definition.Skills));
        command.Parameters.AddWithValue("$browser_profile", (object?)definition.BrowserProfile ?? DBNull.Value);
        command.Parameters.AddWithValue("$memory_scope", (object?)definition.MemoryScope ?? DBNull.Value);
        command.Parameters.AddWithValue("$scheduled_task_permissions_json", JsonSerializer.Serialize(definition.ScheduledTaskPermissions));
        command.Parameters.AddWithValue("$auto_start", definition.AutoStart ? 1 : 0);
    }

    private static AgentDesiredState InitialDesiredState(AgentDefinition definition) =>
        definition.AutoStart ? AgentDesiredState.Running : AgentDesiredState.Stopped;

    private static string ToDatabaseValue(AgentDesiredState state) => state switch
    {
        AgentDesiredState.Running => "running",
        AgentDesiredState.Stopped => "stopped",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private static string ToDatabaseValue(SessionObservedState state) => state switch
    {
        SessionObservedState.Missing => "missing",
        SessionObservedState.Starting => "starting",
        SessionObservedState.Running => "running",
        SessionObservedState.Exited => "exited",
        SessionObservedState.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private static AgentDesiredState ParseDesiredState(string value) => value switch
    {
        "running" => AgentDesiredState.Running,
        "stopped" => AgentDesiredState.Stopped,
        _ => throw new HarnessDatabaseException($"Unknown persisted agent desired state {value}.")
    };

    private static SessionObservedState ParseObservedState(string value) => value switch
    {
        "missing" => SessionObservedState.Missing,
        "starting" => SessionObservedState.Starting,
        "running" => SessionObservedState.Running,
        "exited" => SessionObservedState.Exited,
        "error" => SessionObservedState.Error,
        _ => throw new HarnessDatabaseException($"Unknown persisted session observed state {value}.")
    };

    private static DateTimeOffset? ReadDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : DateTimeOffset.Parse(reader.GetString(ordinal));
}
