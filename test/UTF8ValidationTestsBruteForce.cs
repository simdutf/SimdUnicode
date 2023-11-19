namespace tests;
using System.Text;
using SimdUnicode;

public class Utf8ValidationTests
{
    
    // I save this for when testing the SIMD version
    // [Fact]
    // public void BruteForceTest()
    // {
    //     for (int i = 0; i < NumTrials; i++)
    //     {
    //         byte[] utf8 = generator.Generate(rand.Next(256));
    //         Assert.True(ValidateUtf8(utf8), "UTF-8 validation failed, indicating a bug.");

    //         for (int flip = 0; flip < 1000; flip++)
    //         {
    //             byte[] modifiedUtf8 = (byte[])utf8.Clone();
    //             int byteIndex = rand.Next(modifiedUtf8.Length);
    //             int bitFlip = 1 << rand.Next(8);
    //             modifiedUtf8[byteIndex] ^= (byte)bitFlip;

    //             bool isValid = ValidateUtf8(modifiedUtf8);
    //             // This condition may depend on the specific behavior of your validation method
    //             // and whether or not it should match a reference implementation.
    //             // In this example, we are simply asserting that the modified sequence is still valid.
    //             Assert.True(isValid, "Mismatch in UTF-8 validation detected, indicating a bug.");
    //         }
    //     }
    // }

    // Pseudocode for easier ChatGPT generatioN:
    // 1. Set a seed value (1234).
    // 2. Create a random UTF-8 generator with equal probabilities for 1, 2, 3, and 4-byte sequences.
    // 3. Set the total number of trials to 1000.

    // 4. For each trial (0 to total - 1):
    //    a. Generate a random UTF-8 sequence with a length between 0 and 255.
    //    b. Validate the UTF-8 sequence. If it's invalid:
    //       - Output "bug" to stderr.
    //       - Fail the test.

    //    c. For 1000 times (bit flipping loop):
    //       i. Generate a random bit position (0 to 7).
    //       ii. Flip exactly one bit at the random position in a random byte of the UTF-8 sequence.
    //       iii. Re-validate the modified UTF-8 sequence.
    //       iv. Compare the result of the validation with a reference validation method.
    //       v. If the results differ:
    //          - Output "bug" to stderr.
    //          - Fail the test.

    // 5. If all tests pass, output "OK".

}