using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;

namespace ISBSpecialForcesServer.Controllers;
[Injectable(InjectionType.Singleton)]
public sealed class FikaRaidContext(IServiceProvider services, ILogger<FikaRaidContext> logger)
{
    private bool _reported;
    private object? Service(string name)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);
        return type == null ? null : services.GetService(type);
    }

    public string? EventSession(string? session)
    {
        if (string.IsNullOrWhiteSpace(session))
            return session;
        try
        {
            if (HeadlessClient(session) != null)
                return session;
            var matches = Service("FikaServer.Services.MatchService");
            if (matches == null)
                return session;
            var method = matches.GetType().GetMethod("GetMatchIdByPlayer", [typeof(MongoId)])
                ?? throw new MissingMethodException(matches.GetType().FullName, "GetMatchIdByPlayer");
            var shared = method.Invoke(matches, [new MongoId(session)])?.ToString();
            if (shared != null)
                return shared;
            var saves = services.GetService(typeof(SaveServer)) as SaveServer;
            if (saves == null || !saves.GetProfiles().TryGetValue(new MongoId(session), out var profile))
                return session;
            var characters = profile.CharacterData;
            foreach (var player in new[] { characters?.PmcData?.Id, characters?.ScavData?.Id })
            {
                if (!player.HasValue)
                    continue;
                shared = method.Invoke(matches, [player.Value])?.ToString();
                if (shared != null)
                    return shared;
            }
            return session;
        }
        catch (Exception ex)
        {
            Report(ex);
            return session;
        }
    }

    public string? QuestOwner(string? eventSession)
    {
        if (string.IsNullOrWhiteSpace(eventSession))
            return eventSession;
        try
        {
            var client = HeadlessClient(eventSession);
            var requester = client?.GetType().GetProperty("RequesterSessionID")?.GetValue(client) as string;
            return string.IsNullOrWhiteSpace(requester) ? eventSession : requester;
        }
        catch (Exception ex)
        {
            Report(ex);
        }

        return eventSession;
    }

    public object? RaidInstance(string? eventSession)
    {
        if (string.IsNullOrWhiteSpace(eventSession))
            return null;
        try
        {
            var matches = Service("FikaServer.Services.MatchService");
            var entries = matches?.GetType().GetProperty("Matches")?.GetValue(matches) as IEnumerable;
            if (entries == null)
                return null;
            foreach (var entry in entries)
            {
                var type = entry.GetType();
                if (type.GetProperty("Key")?.GetValue(entry)?.ToString() == eventSession)
                    return type.GetProperty("Value")?.GetValue(entry);
            }
        }
        catch (Exception ex)
        {
            Report(ex);
        }
        return null;
    }

    private object? HeadlessClient(string session)
    {
        var headless = Service("FikaServer.Services.Headless.HeadlessService");
        var clients = headless?.GetType().GetProperty("HeadlessClients")?.GetValue(headless) as IEnumerable;
        if (clients == null)
            return null;
        foreach (var entry in clients)
        {
            var type = entry.GetType();
            if (type.GetProperty("Key")?.GetValue(entry)?.ToString() == session)
                return type.GetProperty("Value")?.GetValue(entry);
        }
        return null;
    }

    private void Report(Exception ex)
    {
        if (_reported)
            return;
        _reported = true;
        logger.LogError(ex, "ISB: could not resolve the Fika raid owner; using the requesting session.");
    }
}
