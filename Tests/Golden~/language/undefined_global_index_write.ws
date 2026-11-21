# T3.8 (PROP-27): an index write reads the name first.
items = {1, 2}
item{0} = 5   # expect-compile-error: undefined variable 'item'
