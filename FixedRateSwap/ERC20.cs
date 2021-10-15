using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace FixedRateSwap
{
    public class Erc20
    {
        private Dictionary<object, BigInteger> Balances { get; } = new();

        public void Mint(object obj, BigInteger amount)
        {
            Balances[obj] = (Balances.TryGetValue(obj, out var x) ? x : 0) + amount;
        }
        
        public void Burn(object obj, BigInteger amount)
        {
            if (BalanceOf(obj) < amount)
            {
                throw new InvalidOperationException("Not enough tokens to burn");
            }
            Balances[obj] = (Balances.TryGetValue(obj, out var x) ? x : 0) - amount;
        }

        public BigInteger BalanceOf(object obj) => Balances.TryGetValue(obj, out var x) ? x : 0;

        public void SafeTransferFrom(object sender, object address, BigInteger amount)
        {
            var senderBalance = BalanceOf(sender);
            if (senderBalance < amount)
            {
                throw new InvalidOperationException("Balance is not enough");
            }

            Mint(address, amount);
            Balances[sender] -= amount;
        }

        public BigInteger TotalSupply() => Balances.Values.Aggregate(BigInteger.Zero, BigInteger.Add);
    }
}