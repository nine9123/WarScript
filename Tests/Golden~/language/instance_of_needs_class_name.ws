# T1.8 (CRASH-6): the right side of `is` must be a class name. Anything else
# is a compile error, not a C# exception escaping Run() (A.23b covers `as`).
class P [n]
end
p = new P [1]
x = p is 3   # expect-compile-error: 'is' must be followed by a class name
