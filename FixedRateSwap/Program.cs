using System;
using System.Numerics;

namespace FixedRateSwap
{
    class Program
    {
        static void Main(string[] args)
        {
            var swapAmount = Ether(1);

            var depositWithdrawalResult = Test(fixedRateSwap =>
            {
                var depositResult = fixedRateSwap.Deposit(swapAmount, BigInteger.Zero);
                var withdrawResult = fixedRateSwap.WithdrawWithRatio(depositResult, BigInteger.Zero);
                return withdrawResult.token1Amount;
            });

            var swapResult = Test(fixedRateSwap => fixedRateSwap.Swap0To1(swapAmount));
            
            Console.WriteLine(depositWithdrawalResult);
            Console.WriteLine(swapResult);
        }

        private static BigInteger Test(Func<FixedRateSwapImpl, BigInteger> action)
        {
            var self = new object();
            var usdt = new Erc20();
            var usdc = new Erc20();
            var fixedRateSwap = new FixedRateSwapImpl(usdt, usdc, self);
            usdt.Mint(self, Ether(2000));
            usdc.Mint(self, Ether(2000));
            fixedRateSwap.Deposit(Ether(1), Ether(1));
            return action(fixedRateSwap);
        }

        private static BigInteger Ether(BigInteger value) => value * BigInteger.Parse("1000000000000000000");
    }
}
