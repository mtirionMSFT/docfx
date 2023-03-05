using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BuildFromCSharpSourceCode;

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
public class CSharp
{
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
    public static void Main(string[] args)
    {
        Console.WriteLine("Hello World!");
    }

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
