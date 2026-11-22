# T3.10 (LANGUAGE.md section 9, plan 5.9): 'raise null' raises the text
# "Empty exception"; any other raised value is rescued as itself.
begin
    raise {1, 2}
rescue e
    print e{1}                                # expect: 2
end
raise null   # expect-error: Raised: Empty exception
