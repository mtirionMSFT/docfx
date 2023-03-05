using System.ComponentModel;

namespace BuildFromAssembly;

/// <summary>
/// This contains indented code.
/// <![CDATA[[
/// ```
/// for (int i = 0; i < 10; i++)
/// {
///     DoSomething();
/// }
/// ```
/// ]]>
/// </summary>
public class Class1
{
    /// <summary>
    /// This contains indented code.
    ///
    ///     for (int i = 0; i &lt; 10; i++)
    ///     {
    ///         DoSomething();
    ///     }
    /// </summary>
    public static void HelloWorld() { }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void HiddenAPI() { }

    /// <summary>
    /// This contains indented code.
    ///
    ///     for (int i = 0; i &lt; 10; i++)
    ///     {
    ///         DoSomething();
    ///     }
    /// </summary>
    public void IndentedCode() {}
    
    /// <summary>
    /// This contains code in a fence.
    ///
    /// ```
    /// for (int i = 0; i &lt; 10; i++)
    /// {
    ///     DoSomething();
    /// }
    /// ```
    /// </summary>
    public void FencedCode() {}
}
