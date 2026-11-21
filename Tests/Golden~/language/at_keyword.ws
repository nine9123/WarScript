# T3.3: `@` followed by a keyword or a literal word is not a field.
class P [x]
    fun f []
        return @end   # expect-compile-error: '@' must be followed by a field name
    end
end
