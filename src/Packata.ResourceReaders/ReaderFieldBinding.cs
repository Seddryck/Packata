namespace Packata.ResourceReaders;

/// <summary>Declares the CLR type a reader binding produces and its reader-specific configuration.</summary>
public sealed record ReaderFieldBinding(Type RuntimeType, object? Configuration = null);
