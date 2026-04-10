# ScriptableObject Rules

## One type per file

Each `ScriptableObject` subclass must live in its own `.cs` file.

Multiple SO types in one file share one GUID — Unity cannot correctly deserialize assets of the non-primary type (they load as null).
