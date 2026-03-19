namespace Tests
{
    public class WarScriptLanguageTest
    {
        /*
        private static string GetResourcePath(string resourceName)
        {
            // Look for test resources relative to the test assembly location
            var assemblyDir = Path.GetDirectoryName(typeof(ToyLanguageTest).Assembly.Location);
            return Path.Combine(assemblyDir!, "resources", resourceName);
        }

        [Fact]
        public void IsSameTree()
        {
            var path = GetResourcePath("is_same_tree.toy");

            var outputStream = new MemoryStream();

            var originalOut = Console.Out;
            var originalErr = Console.Error;

            try
            {
                Console.SetOut(new StreamWriter(outputStream) { AutoFlush = true });
                Console.SetError(new StreamWriter(outputStream) { AutoFlush = true });

                var lang = new WarScriptLanguage();
                lang.Execute(path);

                var output = Encoding.UTF8.GetString(outputStream.ToArray());
                Assert.Equal("", output);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }

        [Fact]
        public void BinarySearch()
        {
            var path = GetResourcePath("binary_search.toy");

            var outputStream = new MemoryStream();

            var originalOut = Console.Out;
            var originalErr = Console.Error;

            try
            {
                Console.SetOut(new StreamWriter(outputStream) { AutoFlush = true });
                Console.SetError(new StreamWriter(outputStream) { AutoFlush = true });

                var lang = new WarScriptLanguage();
                lang.Execute(path);

                var output = Encoding.UTF8.GetString(outputStream.ToArray());
                Assert.Equal("", output);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }

        [Fact]
        public void BubbleSort()
        {
            var path = GetResourcePath("bubble_sort.toy");

            var outputStream = new MemoryStream();

            var originalOut = Console.Out;
            var originalErr = Console.Error;

            try
            {
                Console.SetOut(new StreamWriter(outputStream) { AutoFlush = true });
                Console.SetError(new StreamWriter(outputStream) { AutoFlush = true });

                var lang = new WarScriptLanguage();
                lang.Execute(path);

                var output = Encoding.UTF8.GetString(outputStream.ToArray());
                Assert.Equal("", output);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }

        [Fact]
        public void Stack()
        {
            var path = GetResourcePath("stack.toy");

            var outputStream = new MemoryStream();

            var originalOut = Console.Out;
            var originalErr = Console.Error;

            try
            {
                Console.SetOut(new StreamWriter(outputStream) { AutoFlush = true });
                Console.SetError(new StreamWriter(outputStream) { AutoFlush = true });

                var lang = new WarScriptLanguage();
                lang.Execute(path);

                var output = Encoding.UTF8.GetString(outputStream.ToArray());
                Assert.Equal("", output);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }

        [Fact]
        public void InstanceOf()
        {
            var path = GetResourcePath("instance_of.toy");

            var outputStream = new MemoryStream();

            var originalOut = Console.Out;
            var originalErr = Console.Error;

            try
            {
                Console.SetOut(new StreamWriter(outputStream) { AutoFlush = true });
                Console.SetError(new StreamWriter(outputStream) { AutoFlush = true });

                var lang = new WarScriptLanguage();
                lang.Execute(path);

                var output = Encoding.UTF8.GetString(outputStream.ToArray());
                Assert.Equal("", output);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }

        [Fact]
        public void CastType()
        {
            var path = GetResourcePath("cast_type.toy");

            var outputStream = new MemoryStream();

            var originalOut = Console.Out;
            var originalErr = Console.Error;

            try
            {
                Console.SetOut(new StreamWriter(outputStream) { AutoFlush = true });
                Console.SetError(new StreamWriter(outputStream) { AutoFlush = true });

                var lang = new WarScriptLanguage();
                lang.Execute(path);

                var output = Encoding.UTF8.GetString(outputStream.ToArray());
                Assert.Equal("", output);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }

        [Fact]
        public void Calculator()
        {
            var path = GetResourcePath("calculator.toy");

            var outputStream = new MemoryStream();

            var originalOut = Console.Out;
            var originalErr = Console.Error;

            try
            {
                Console.SetOut(new StreamWriter(outputStream) { AutoFlush = true });
                Console.SetError(new StreamWriter(outputStream) { AutoFlush = true });

                var lang = new WarScriptLanguage();
                lang.Execute(path);

                var output = Encoding.UTF8.GetString(outputStream.ToArray());
                Assert.Equal("", output);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }

        [Fact]
        public void RaiseException()
        {
            var path = GetResourcePath("raise_exception.toy");

            var outputStream = new MemoryStream();

            var originalOut = Console.Out;
            var originalErr = Console.Error;

            try
            {
                Console.SetOut(new StreamWriter(outputStream) { AutoFlush = true });
                Console.SetError(new StreamWriter(outputStream) { AutoFlush = true });

                var lang = new WarScriptLanguage();
                lang.Execute(path);

                var output = Encoding.UTF8.GetString(outputStream.ToArray());
                Assert.Equal(
                    "Do something useful ...\n" +
                    "MyBusinessException [ message = A message that describes the error. ]\n" +
                    "    at do_something_else:14\n" +
                    "    at perform_business_operation:5\n" +
                    "    at raise_exception.toy:1\n",
                    output
                );
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }

        [Fact]
        public void HandleException()
        {
            var path = GetResourcePath("handle_exception.toy");

            var outputStream = new MemoryStream();

            var originalOut = Console.Out;
            var originalErr = Console.Error;

            try
            {
                Console.SetOut(new StreamWriter(outputStream) { AutoFlush = true });
                Console.SetError(new StreamWriter(outputStream) { AutoFlush = true });

                var lang = new WarScriptLanguage();
                lang.Execute(path);

                var output = Encoding.UTF8.GetString(outputStream.ToArray());
                Assert.Equal(
                    "Do something useful ...\n" +
                    "Rescuing 'A message that describes the error.'\n" +
                    "Ensure block\n",
                    output
                );
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }
        */
    }
}
