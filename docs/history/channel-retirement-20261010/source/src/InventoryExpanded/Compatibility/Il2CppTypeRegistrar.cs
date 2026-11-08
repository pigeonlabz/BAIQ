#if IL2CPP
using Il2CppInterop.Runtime.Injection;
#endif

namespace Backpack.Compatibility;

internal static class Il2CppTypeRegistrar
{
    // Keep this list in IL2CPP parent-before-child order. PlayerBackpack is the
    // only Inventory Expanded managed type attached as an IL2CPP component.
    private static readonly string[] RequiredTypeNames =
    [
        "Backpack.PlayerBackpack"
    ];

    private static readonly object RegistrationLock = new();
    private static RegistrationState _state;
    private static string _failure;

    internal static IReadOnlyList<string> RequiredTypes => RequiredTypeNames;

    public static bool TryRegisterRequiredTypes(out string error)
    {
#if IL2CPP
        return TryRegisterAll(RegisterType, out error);
#else
        error = null;
        return true;
#endif
    }

    internal static bool TryRegisterAll(Action<string> register, out string error)
    {
        if (register == null)
        {
            error = "No IL2CPP type registration callback was supplied.";
            return false;
        }

        lock (RegistrationLock)
        {
            if (_state == RegistrationState.Complete)
            {
                error = null;
                return true;
            }

            if (_state == RegistrationState.Failed)
            {
                error = _failure;
                return false;
            }

            if (_state == RegistrationState.Registering)
            {
                error = "IL2CPP custom type registration was re-entered before it completed.";
                return false;
            }

            _state = RegistrationState.Registering;
            try
            {
                foreach (var typeName in RequiredTypeNames)
                    register(typeName);

                _state = RegistrationState.Complete;
                _failure = null;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                _state = RegistrationState.Failed;
                _failure = $"{exception.GetType().Name}: {exception.Message}";
                error = _failure;
                return false;
            }
        }
    }

#if IL2CPP
    private static void RegisterType(string typeName)
    {
        if (string.Equals(typeName, typeof(PlayerBackpack).FullName, StringComparison.Ordinal))
        {
            ClassInjector.RegisterTypeInIl2Cpp<PlayerBackpack>();
            return;
        }

        throw new InvalidOperationException($"No explicit IL2CPP registrar is defined for required type '{typeName}'.");
    }
#endif

    internal static void ResetForTests()
    {
        lock (RegistrationLock)
        {
            _state = RegistrationState.NotStarted;
            _failure = null;
        }
    }

    private enum RegistrationState
    {
        NotStarted,
        Registering,
        Complete,
        Failed
    }
}
