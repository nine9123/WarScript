# Instantiating a class that does not exist is a NameError (plan T2.5).
u = new Nope []   # expect-error: NameError: Class 'Nope' is not defined
