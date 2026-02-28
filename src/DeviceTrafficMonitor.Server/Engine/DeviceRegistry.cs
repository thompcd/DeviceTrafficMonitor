using System.Collections.Concurrent;
using DeviceTrafficMonitor.Core.Models;
using Microsoft.Extensions.Configuration;

namespace DeviceTrafficMonitor.Server.Engine;

public class DeviceRegistry
{
    private readonly ConcurrentDictionary<string, DeviceRegistration> _devices = new();

    public void LoadFromConfig(IConfiguration config)
    {
        var devices = config.GetSection("Devices").Get<DeviceConfig[]>() ?? [];
        foreach (var device in devices)
        {
            _devices.TryAdd(device.Id, new DeviceRegistration(device, "config"));
        }
    }

    public void Add(DeviceConfig config, string source)
    {
        if (!_devices.TryAdd(config.Id, new DeviceRegistration(config, source)))
        {
            throw new InvalidOperationException($"Device '{config.Id}' is already registered.");
        }
    }

    public bool Remove(string id) => _devices.TryRemove(id, out _);

    public bool Exists(string id) => _devices.ContainsKey(id);

    public DeviceConfig[] GetAll() => _devices.Values.Select(r => r.Config).ToArray();

    public DeviceConfig[] GetByIds(string[] ids)
    {
        var result = new List<DeviceConfig>();
        foreach (var id in ids)
        {
            if (!_devices.TryGetValue(id, out var reg))
                throw new KeyNotFoundException($"Device '{id}' is not registered.");
            result.Add(reg.Config);
        }
        return result.ToArray();
    }

    public DeviceRegistration GetRegistration(string id)
    {
        if (!_devices.TryGetValue(id, out var reg))
            throw new KeyNotFoundException($"Device '{id}' is not registered.");
        return reg;
    }

    public IReadOnlyList<DeviceRegistration> GetAllRegistrations() => _devices.Values.ToList();
}
