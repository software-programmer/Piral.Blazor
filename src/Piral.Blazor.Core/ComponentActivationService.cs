using Microsoft.Extensions.Logging;
using Piral.Blazor.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

namespace Piral.Blazor.Core
{
    public class ComponentActivationService : IComponentActivationService
    {
        private readonly Dictionary<string, Type> _services = new Dictionary<string, Type>();

        private readonly List<ActiveComponent> _active = new List<ActiveComponent>();
        private readonly ILogger<ComponentActivationService> _logger;
        private readonly IModuleContainerService _container;

        public event EventHandler Changed;

        public IEnumerable<ActiveComponent> Components => _active;

        private static readonly IReadOnlyCollection<Type> AttributeTypes = new List<Type>
        {
            typeof(PiralComponentAttribute),
            typeof(PiralExtensionAttribute),
            typeof(ExposePiletAttribute),
            typeof(RouteAttribute)
        };

        public ComponentActivationService(IModuleContainerService container, ILogger<ComponentActivationService> logger)
        {
            _container = container;
            _logger = logger;
            JSBridge.ActivationService = this;
        }

        public void Register(string componentName, Type componentType, IServiceProvider provider, WebAssemblyHost host)
        {
            if (_services.ContainsKey(componentName))
            {
                _logger.LogWarning("The provided component name has already been registered.");
            }
            else
            {
                _services.Add(componentName, componentType);
                _container.ConfigureComponent(componentType, provider, host);
            }
        }

        public void Unregister(string componentName)
        {
            if (_services.TryGetValue(componentName, out var componentType))
            {
                DeactivateComponent(componentName);
                _services.Remove(componentName);
                _container.ForgetComponent(componentType);
            }
            else
            {
                _logger.LogWarning("The provided component name has not been registered.");
            }
        }

        public void ActivateComponent(string componentName, string referenceId, IDictionary<string, object> args)
        {
            var component = GetComponent(componentName);
            try
            {
                _active.Add(new ActiveComponent(componentName, referenceId, component, args));
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (ArgumentException ae)
            {
                _logger.LogError($"One of the arguments is invalid: {ae.Message}");
            }
        }

        public void DeactivateComponent(string componentName, string referenceId)
        {
            var removed = _active.RemoveAll(m => m.ComponentName == componentName && m.ReferenceId == referenceId);

            if (removed > 0)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        public void DeactivateComponent(string componentName)
        {
            var removed = _active.RemoveAll(m => m.ComponentName == componentName);

            if (removed > 0)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        public void LoadComponentsFromAssembly(Assembly assembly, WebAssemblyHost host)
        {
            var serviceProvider = _container.Configure(assembly);
            var componentTypes = assembly.GetTypesWithAttributes(AttributeTypes);

            foreach (var componentType in componentTypes)
            {
                var componentNames = GetComponentNamesToRegister(componentType, AttributeTypes);
                foreach (var componentName in componentNames)
                {
                    Register(componentName, componentType, serviceProvider, host);
                    _logger.LogInformation($"registered {componentName}");
                }
            }
        }

        private Type GetComponent(string componentName)
        {
            _services.TryGetValue(componentName, out var value);
            return value;
        }

        private static IEnumerable<string> GetComponentNamesToRegister(Type member, IEnumerable<Type> attributeTypes)
        {
            return attributeTypes.Select(at => GetComponentNameToRegister(member, at)).Where(val => val != null);
        }

        private static string GetComponentNameToRegister(Type member, Type attributeType)
        {
            // get only the first occurence of the attribute.
            // This is only relevant for extensions, which can have multiple attributes,
            // but the name to register (FQN) will be the same for every occurence anyway.
            var attribute = member
                .GetCustomAttributes(attributeType, false)
                .FirstOrDefault(); 
            
            if (attribute is null)
            {
                return null;
            }
            
            return attributeType switch
            {
                Type _ when attributeType == typeof(RouteAttribute) =>
                    $"page-{((RouteAttribute) attribute).Template}",
                Type _ when attributeType == typeof(PiralExtensionAttribute) => 
                    $"extension-{member.FullName}",
                Type _ when attributeType == typeof(PiralComponentAttribute) =>
                    $"{((PiralComponentAttribute) attribute).Name ?? member.FullName}",
                Type _ when attributeType == typeof(ExposePiletAttribute) =>
                    $"{((ExposePiletAttribute) attribute).Name ?? member.FullName}",
                _ => null
            };
        }
    }
}
