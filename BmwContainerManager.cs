using System;
using System.Collections.Generic;
using System.Linq;

namespace BmwCarDataClient
{
    public class BmwContainer
    {
        public string Name { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public List<string> TechnicalDescriptors { get; set; } = new();
    }

    public static class BmwContainerManager
    {
        public static List<BmwContainer> BuildContainers(IEnumerable<string> keys)
        {
            var categories = new[]
            {
                "Cabin", "Drivetrain", "Powertrain", "Body", "Vehicle",
                "Chassis", "Trip", "Status", "Channel", "ElectricalSystem"
            };

            var containers = categories.Select(cat => new BmwContainer
            {
                Name = cat,
                Purpose = $"App Subscription Tier - {cat}",
                TechnicalDescriptors = new List<string>()
            }).ToList();

            foreach (var key in keys)
            {
                var trimmedKey = key.Trim();
                if (string.IsNullOrEmpty(trimmedKey)) continue;

                string category = "Vehicle"; // Default fallback

                if (trimmedKey.StartsWith("vehicle?.cabin?.", StringComparison.OrdinalIgnoreCase))
                    category = "Cabin";
                else if (trimmedKey.StartsWith("vehicle?.drivetrain?.", StringComparison.OrdinalIgnoreCase))
                    category = "Drivetrain";
                else if (trimmedKey.StartsWith("vehicle?.powertrain?.", StringComparison.OrdinalIgnoreCase))
                    category = "Powertrain";
                else if (trimmedKey.StartsWith("vehicle?.body?.", StringComparison.OrdinalIgnoreCase))
                    category = "Body";
                else if (trimmedKey.StartsWith("vehicle?.chassis?.", StringComparison.OrdinalIgnoreCase))
                    category = "Chassis";
                else if (trimmedKey.StartsWith("vehicle?.trip?.", StringComparison.OrdinalIgnoreCase))
                    category = "Trip";
                else if (trimmedKey.StartsWith("vehicle?.status?.", StringComparison.OrdinalIgnoreCase))
                    category = "Status";
                else if (trimmedKey.StartsWith("vehicle?.channel?.", StringComparison.OrdinalIgnoreCase))
                    category = "Channel";
                else if (trimmedKey.StartsWith("vehicle?.electricalSystem?.", StringComparison.OrdinalIgnoreCase) ||
                         trimmedKey.StartsWith("vehicle?.electronicControlUnit?.", StringComparison.OrdinalIgnoreCase))
                    category = "ElectricalSystem";
                else if (trimmedKey.StartsWith("vehicle?.vehicle?.", StringComparison.OrdinalIgnoreCase) ||
                         trimmedKey.Equals("vehicle?.isMoving", StringComparison.OrdinalIgnoreCase))
                    category = "Vehicle";

                var targetContainer = containers.FirstOrDefault(c => c.Name.Equals(category, StringComparison.OrdinalIgnoreCase));
                if (targetContainer != null)
                {
                    targetContainer.TechnicalDescriptors.Add(trimmedKey);
                }
            }

            return containers;
        }
    }
}
