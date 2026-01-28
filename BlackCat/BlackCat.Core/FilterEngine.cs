using BlackCat.Shared.Models;
using BlackCat.Shared.Enums;
using System.Net;

namespace BlackCat.Core;

/// <summary>
/// Двигун фільтрації пакетів
/// </summary>
public class FilterEngine
{
    private readonly List<FilterRule> _rules = new();
    private readonly object _lockObject = new();
    private bool _defaultAllow = false; // За замовчуванням - блокувати все

    public event EventHandler<FilterDecisionEventArgs>? PacketFiltered;

    /// <summary>
    /// Чи дозволяти пакети за замовчуванням (якщо жодне правило не підійшло)
    /// </summary>
    public bool DefaultAllow
    {
        get => _defaultAllow;
        set => _defaultAllow = value;
    }

    /// <summary>
    /// Завантажити правила
    /// </summary>
    public void LoadRules(IEnumerable<FilterRule> rules)
    {
        lock (_lockObject)
        {
            _rules.Clear();
            _rules.AddRange(rules.Where(r => r.IsEnabled).OrderBy(r => r.Priority));
        }
    }

    /// <summary>
    /// Додати правило
    /// </summary>
    public void AddRule(FilterRule rule)
    {
        lock (_lockObject)
        {
            _rules.Add(rule);
            _rules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }
    }

    /// <summary>
    /// Видалити правило
    /// </summary>
    public void RemoveRule(int ruleId)
    {
        lock (_lockObject)
        {
            _rules.RemoveAll(r => r.Id == ruleId);
        }
    }

    /// <summary>
    /// Перевірити пакет і прийняти рішення
    /// </summary>
    public FilterDecision CheckPacket(PacketInfo packet, TrafficDirection direction)
    {
        lock (_lockObject)
        {
            foreach (var rule in _rules)
            {
                // Перевірка напрямку
                if (rule.Direction != TrafficDirection.Both && rule.Direction != direction)
                    continue;

                // Перевірка протоколу
                if (rule.Protocol != BlackCat.Shared.Models.ProtocolType.Any &&
                    rule.Protocol != packet.Protocol)
                    continue;

                // Перевірка IP адреси
                if (!string.IsNullOrEmpty(rule.IPAddress))
                {
                    bool ipMatches = false;

                    if (rule.IPAddress.Contains('/'))
                    {
                        // CIDR нотація (наприклад 192.168.1.0/24)
                        ipMatches = IsIPInSubnet(packet.SourceIP, rule.IPAddress) ||
                                   IsIPInSubnet(packet.DestinationIP, rule.IPAddress);
                    }
                    else
                    {
                        // Точна відповідність
                        ipMatches = packet.SourceIP == rule.IPAddress ||
                                   packet.DestinationIP == rule.IPAddress;
                    }

                    if (!ipMatches)
                        continue;
                }

                // Перевірка порту
                if (rule.Port != 0)
                {
                    if (packet.SourcePort != rule.Port && packet.DestinationPort != rule.Port)
                        continue;
                }

                // Правило підійшло!
                var decision = new FilterDecision
                {
                    Action = rule.Action,
                    MatchedRule = rule,
                    Packet = packet
                };

                PacketFiltered?.Invoke(this, new FilterDecisionEventArgs(decision));
                return decision;
            }

            // Жодне правило не підійшло - використати дефолтну поведінку
            var defaultDecision = new FilterDecision
            {
                Action = _defaultAllow ? FilterAction.Allow : FilterAction.Block,
                MatchedRule = null,
                Packet = packet
            };

            PacketFiltered?.Invoke(this, new FilterDecisionEventArgs(defaultDecision));
            return defaultDecision;
        }
    }

    /// <summary>
    /// Перевірка чи IP належить підмережі (CIDR)
    /// </summary>
    private bool IsIPInSubnet(string ipAddress, string cidr)
    {
        try
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2) return false;

            string networkAddress = parts[0];
            int prefixLength = int.Parse(parts[1]);

            var ip = IPAddress.Parse(ipAddress);
            var network = IPAddress.Parse(networkAddress);

            byte[] ipBytes = ip.GetAddressBytes();
            byte[] networkBytes = network.GetAddressBytes();

            if (ipBytes.Length != networkBytes.Length)
                return false;

            int maskBits = prefixLength;
            for (int i = 0; i < ipBytes.Length; i++)
            {
                byte mask = maskBits >= 8 ? (byte)255 :
                           maskBits > 0 ? (byte)(255 << (8 - maskBits)) :
                           (byte)0;

                if ((ipBytes[i] & mask) != (networkBytes[i] & mask))
                    return false;

                maskBits -= 8;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Отримати всі правила
    /// </summary>
    public IReadOnlyList<FilterRule> GetRules()
    {
        lock (_lockObject)
        {
            return _rules.ToList().AsReadOnly();
        }
    }
}

/// <summary>
/// Рішення фільтрації
/// </summary>
public class FilterDecision
{
    public FilterAction Action { get; set; }
    public FilterRule? MatchedRule { get; set; }
    public PacketInfo Packet { get; set; } = null!;
}

public class FilterDecisionEventArgs : EventArgs
{
    public FilterDecision Decision { get; }

    public FilterDecisionEventArgs(FilterDecision decision)
    {
        Decision = decision;
    }
}
