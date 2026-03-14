namespace WarScript.Token
{
    /// <summary>
    /// Lexeme types with matching regex expression
    ///
    /// <see cref="Token"/>
    /// <see cref="LexicalParser"/>
    /// </summary>
    public enum TokenType
    {
        /// <summary>
        /// Comment
        /// </summary>
        Comment,
        
        /// <summary>
        /// Line break
        /// </summary>
        LineBreak,
        
        /// <summary>
        /// Whitespace
        /// </summary>
        Whitespace,
        
        /// <summary>
        /// Words with a specific sense assigned by the compiler
        /// <para>1. Conditions: <code>if, elif, else, end</code></para>
        /// <para>2. Printing to a console: <code>print</code></para>
        /// <para>3. Defining a class: <code>class</code></para>
        /// <para>4. Defining a function: <code>fun, return</code></para>
        /// <para>5. Loops: <code>loop, in, by, break, next</code></para>
        /// <para>6. Asserting a value: <code>assert</code></para>
        /// <para>7. Raising and handling exceptions: <code>raise, begin, rescue, ensure</code></para>
        /// </summary>
        Keyword,
        
        /// <summary>
        /// Dividers for the different lexeme groups
        /// <para>1. Defining a class or a function properties: <code>[ ]</code></para>
        /// <para>2. Counting multiple values: <code>,</code></para>
        /// <para>3. Defining an array values: <code>{ }</code></para>
        /// <para>4. Splitting a loop range: <code>..</code></para>
        /// <para>5. Splitting Derived class from inherited Base types: <code>:</code></para>
        /// </summary>
        GroupDivider,
        
        /// <summary>
        /// Logical
        /// </summary>
        Logical,
        
        /// <summary>
        /// Numeric
        /// </summary>
        Numeric,
        
        /// <summary>
        /// Null
        /// </summary>
        Null,
        
        /// <summary>
        /// This reference
        /// </summary>
        This,
        
        /// <summary>
        /// Text value in quotes
        /// </summary>
        Text,
        
        /// <summary>
        /// Operators
        /// <para>1. Addition <code>+</code></para>
        /// <para>2. Subtraction <code>-</code></para>
        /// <para>3. Multiplication <code>*</code> and Exponentiation <code>**</code></para>
        /// <para>4. Division <code>/</code> and Floor division <code>//</code></para>
        /// <para>5. Modulo <code>%</code></para>
        /// <para>6. Greater than <code>></code></para>
        /// <para>7. Greater than or equal to <code>>=</code></para>
        /// <para>8. Less than <code>&lt;</code></para>
        /// <para>9. Less than or equal to <code>&lt;=</code></para>
        /// <para>10. Append a value to array <code>&lt;&lt;</code></para>
        /// <para>11. Equal <code>=</code></para>
        /// <para>12. Equal to <code>==</code> and not equal to <code>!=</code></para>
        /// <para>13. Not operator <code>!</code></para>
        /// <para>14. Creating an instance of a nested class: <code>:: new</code></para>
        /// <para>15. Accessing class's property or class's function: <code>::</code></para>
        /// <para>16. Creating an instance of a class: <code>new</code></para>
        /// <para>17. And operator <code>and</code></para>
        /// <para>18. Or operator <code>or</code></para>
        /// <para>19. Cast operator <code>as</code></para>
        /// <para>20. Instance of operator <code>is</code></para>
        /// </summary>
        Operator,
        
        /// <summary>
        /// Variable
        /// </summary>
        Variable,
    }
}