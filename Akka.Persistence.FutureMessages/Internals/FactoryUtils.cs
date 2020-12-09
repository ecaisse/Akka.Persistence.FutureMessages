using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Akka.Persistence.FutureMessages.Internals
{
    internal static class FactoryUtils
    {
        internal static T Create<T>(Type type, params FactoryParameter[] potentialParameters)
        {
            if (type.IsInterface || type.IsAbstract)
            {
                throw new ArgumentException($"Type {type.Name} is an interface or is abstract and cannot be constructed.", nameof(type));
            }

            if (!typeof(T).IsAssignableFrom(type))
            {
                throw new ArgumentException($"Type {type.Name} cannot be cast to {typeof(T).Name}.", nameof(type));
            }

            var map = potentialParameters.ToDictionary(x => x.Type, x => x.Value);
            var res = FindConstructorWithParameters(type, map);
            if (res.Constructor == null)
            {
                throw new MissingMethodException(type.FullName, ".ctor");
            }

            return (T)res.Constructor.Invoke(res.ParameterValues);
        }

        internal static FactoryParameter AsFactoryParameter<T>(this T value) => new FactoryParameter(value == null ? typeof(T) : value.GetType(), value);

        private static (ConstructorInfo Constructor, object[] ParameterValues) FindConstructorWithParameters(Type type, Dictionary<Type, object> map)
        {
            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            ConstructorInfo candidate = null;
            object[] candidateParameters = null;
            foreach (var constructor in constructors)
            {
                var parameters = constructor.GetParameters();
                if (parameters.Length <= map.Count)
                {
                    bool done = true;
                    var parameterValues = new object[parameters.Length];
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var param = parameters[i];
                        if (map.TryGetValue(param.ParameterType, out var value))
                        {
                            parameterValues[i] = value;
                        }
                        else
                        {
                            var assignable = map.Keys.Where(param.ParameterType.IsAssignableFrom).Take(2).ToList();
                            if (assignable.Count == 1)
                            {
                                parameterValues[i] = map[assignable[0]];
                            }
                            else
                            {
                                done = false;
                                break;
                            }
                        }
                    }

                    if (done)
                    {
                        candidate = constructor;
                        candidateParameters = parameterValues;
                    }
                }
            }

            return (candidate, candidateParameters);
        }

        internal sealed class FactoryParameter
        {
            public FactoryParameter(Type type, object value)
            {
                Type = type;
                Value = value;
            }

            public Type Type { get; }
            public object Value { get; }
        }
    }
}
