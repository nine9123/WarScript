# Calling a method the class does not have is a NameError (plan T2.5).
# Another class has it, so only the VM can tell (T3.8, PROP-29).
class Point [x, y]
end
class Other
    fun nope []
    end
end
p = new Point [1, 2]
p :: nope []   # expect-error: NameError: Method 'nope' with 0 args not found
