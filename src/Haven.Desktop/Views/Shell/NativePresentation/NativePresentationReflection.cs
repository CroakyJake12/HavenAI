using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;

namespace Haven.Desktop.Views.Shell.NativePresentation;

/// <summary>
/// Small, audited bridge used while native code-behind surfaces wrap existing
/// application services and view-models. It never swallows exceptions from
/// actual commands; callers receive the failure and render it to the user.
/// </summary>
internal static class NativePresentationReflection
{
    private const BindingFlags PublicInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;

    private const BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

    public static object? Get(object? target, params string[] names)
    {
        if (target is null)
        {
            return null;
        }

        var type = target.GetType();
        foreach (var name in names)
        {
            var property = type.GetProperty(name, PublicInstance);
            if (property is not null && property.GetIndexParameters().Length == 0)
            {
                return property.GetValue(target);
            }

            var field = type.GetField(name, AnyInstanc);
            if (field is not null)
            {
                return field.GetValue(target);
            }
        }

        return null;
    }

    public static bool Set(object? target, object? value, params string[] names)
    {
        if (target is null)
        {
            return false;
        }

        var type = target.GetType();
        foreach (var name in names)
        {
            var property = type.GetProperty(name, PublicInstance);
            if (property is null || !property.CanWrite || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (!TryConvert(value, property.PropertyType, out var converted))
            {
                continue;
            }

            property.SetValue(target, converted);
            return true;
        }

        foreach (var name in names)
        {
            var field = type.GetField(name, AnyInstanc);
            if (field is null || field.IsInitOnly)
            {
                continue;
            }

            if (!TryConvert(value, field.FieldType, out var converted))
            {
                continue;
            }

            field.SetValue(target, converted);
            return true;
        }

        return false;
    }

    public static IEnumerable<object> Enumerate(object? value)
    {
        if (value is null || value is string)
        {
            yield break;
        }

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Value is not null)
                {
                    yield return entry.Value;
                }
            }

            yield break;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is not null)
                {
                    yield return item;
                }
            }
        }
    }

    public static string Text(object? target, string fallback, params string[] names)
    {
        var value = Get(target, names);
        var text = value?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    public static Guid? Identifier(object? target, params string[] names)
    {
        var value = Get(target, names);
        return value switch
        {
            Guid guid when guid != Guid.Empty => guid,
            string text when Guid.TryParse(text, out var guid) && guid != Guid.Empty => guid,
            _ => null
        };
    }

    public static DateTimeOffset? Timestamp(object? target, params string[] names)
    {
        var value = Get(target, names);
        return value switch
        {
            DateTimeOffset timestamp => timestamp,
            DateTime dateTime => new DateTimeOffset(dateTime),
            string text when DateTimeOffset.TryParse(text, out var timestamp) => timestamp,
            _ => null
        };
    }

    public static bool Boolean(object? target, bool fallback, params string[] names)
    {
        var value = Get(target, names);
        return value switch
        {
            bool flag => flag,
            string text when bool.TryParse(text, out var flag) => flag,
            _ => fallback
        };
    }

    public static async Task<bool> ExecuteCommandAsync(
        object? target,
        object? parameter,
        params string[] commandNames)
    {
        if (target is null)
        {
            return false;
        }

        foreach (var name in commandNames)
        {
            var candidate = Get(target, name);
            if (candidate is ICommand command)
            {
                if (!command.CanExecute(parameter))
                {
                    return false;
                }

                command.Execute(parameter);
                await ObserveAsyncCommandCompletionAsync(candidate).ConfigureAwait(false);
                return true;
            }

            if (candidate is Delegate callback)
            {
                var parameters = callback.Method.GetParameters();
                if (parameters.Length > 1)
                {
                    continue;
                }

                var result = parameters.Length == 0
                    ? callback.DynamicInvoke()
                    : callback.DynamicInvoke(parameter);
                await AwaitResultAsync(result).ConfigureAwait(false);
                return true;
            }
        }

        return false;
    }

    public static async Task<(bool Invoked, object? Result)> InvokeAsync(
        object? target,
        IEnumerable<string> methodNames,
        params object?[] suppliedArguments)
    {
        if (target is null)
        {
            return (false, null);
        }

        var type = target.GetType();
        foreach (var methodName in methodNames)
        {
            var methods = type
                .GetMethods(AnyInstanc)
                .Where(method => string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(method => method.GetParameters().Length);

            foreach (var method in methods)
            {
                if (!TryBuildArguments(method.GetParameters(), suppliedArguments, out var arguments))
                {
                    continue;
                }

                var result = method.Invoke(target, arguments);
                return (true, await AwaitResultAsync(result).ConfigureAwait(false));
            }
        }

        return (false, null);
    }

    public static IEnumerable<object> ReadCollection(object? target, params string[] names)
    {
        foreach (var name in names)
        {
            var value = Get(target, name);
            var items = Enumerate(value).ToArray();
            if (items.Length > 0)
            {
                return items;
            }
        }

        return Array.Empty<object>();
    }

    public static INotifyPropertyChanged? NotifySource(object? target) => target as INotifyPropertyChanged;

    private static async Task ObserveAsyncCommandCompletionAsync(object command)
    {
        // Common command implementations expose ExecutionTask or CurrentExecution.
        // Await it when available; plain ICommand remains fire-and-observe through
        // the owning view-model's IsBusy/Status properties.
        var taskValue = Get(command, "ExecutionTask", "CurrentExecution", "LastExecution");
        await AwaitResultAsync(taskValue).ConfigureAwait(false);
    }

    private static async Task<object?> AwaitResultAsync(object? result)
    {
        if (result is null)
        {
            return null;
        }

        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            return task.GetType().IsGenericType
                ? task.GetType().GetProperty("Result", PublicInstance)?.GetValue(task)
                : null;
        }

        var type = result.GetType();
        if (type.FullName?.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal) == true)
        {
            var asTask = type.GetMethod("AsTask", PublicInstance);
            if (asTask?.Invoke(result, null) is Task valueTask)
            {
                await valueTask.ConfigureAwait(false);
                return valueTask.GetType().IsGenericType
                    ? valueTask.GetType().GetProperty("Result", PublicInstanc)?.GetValue(valueTask)
                    : null;
            }
        }

        return result;
    }

    private static bool TryBuildArguments(
        IReadOnlyList<ParameterInfo> parameters,
        IReadOnlyList<object?> suppliedArguments,
        out object?[] arguments)
    {
        arguments = new object?[parameters.Count];
        var suppliedIndex = 0;

        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            if (suppliedIndex < suppliedArguments.Count &&
                TryConvert(suppliedArguments[suppliedIndex], parameter.ParameterType, out var converted))
            {
                arguments[index] = converted;
                suppliedIndex++;
                continue;
            }

            if (parameter.ParameterType == typeof(CancellationToken))
            {
                arguments[index] = CancellationToken.None;
                continue;
            }

            if (parameter.HasDefaultValue)
            {
                arguments[index] = parameter.DefaultValue;
                continue;
            }

            return false;
        }

        return suppliedIndex == suppliedArguments.Count;
    }

    private static bool TryConvert(object? value, Type targetType, out object? converted)
    {
        var nullableTarget = Nullable.GetUnderlyingType(targetType);
        var effectiveType = nullableTarget ?? targetType;

        if (value is null)
        {
            converted = null;
            return !effectiveType.IsValueType || nullableTarget is not null;
        }

        if (targetType.IsInstanceOfType(value))
        {
            converted = value;
            return true;
        }

        if (effectiveType.IsEnum && value is string enumText &&
            Enum.TryParse(effectiveType, enumText, true, out var enumValue))
        {
            converted = enumValue;
            return true;
        }

        try
        {
            converted = Convert.ChangeType(value, effectiveType);
            return true;
        }
        catch (Exception)
        {
            converted = null;
            return false;
        }
    }
}
