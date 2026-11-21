# Calling a method the class does not have is a NameError (plan T2.5).
class Point [x, y]
end
p = new Point [1, 2]
p :: nope []   # expect-error: NameError: Method 'nope' with 0 args not found
