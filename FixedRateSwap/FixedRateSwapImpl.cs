using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace FixedRateSwap
{
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    public class FixedRateSwapImpl : Erc20
    {
        private readonly Erc20 _token0;
        private readonly Erc20 _token1;
        private readonly object _msgSender;
        private readonly int _threshold;

        private static readonly BigInteger One = BigInteger.Parse("1000000000000000000");
        private static readonly BigInteger C1 = BigInteger.Parse("999900000000000000");
        private static readonly BigInteger C2 = BigInteger.Parse("3382712334998325432");
        private static readonly BigInteger C3 = BigInteger.Parse("456807350974663119");

        public FixedRateSwapImpl(Erc20 token0, Erc20 token1, object msgSender)
        {
            this._token0 = token0;
            this._token1 = token1;
            _msgSender = msgSender;
            _threshold = 1;
        }

        public BigInteger GetReturn(Erc20 tokenFrom, Erc20 tokenTo, BigInteger inputAmount)
        {
            var fromBalance = tokenFrom.BalanceOf(this);
            var toBalance = tokenTo.BalanceOf(this);
            if (inputAmount > toBalance)
            {
                throw new Exception("input amount is too big");
            }

            return _getReturn(fromBalance, toBalance, inputAmount);
        }

        private BigInteger _getReturn(BigInteger fromBalance, BigInteger toBalance, BigInteger inputAmount)
        {
            var totalBalance = fromBalance + toBalance;
            var x0 = One * fromBalance / totalBalance;
            var x1 = One * (fromBalance + inputAmount) / totalBalance;
            var scaledInputAmount = One * inputAmount;
            var amountMultiplier =
            (
                C1 * scaledInputAmount / totalBalance +
                C2 * PowerHelper(x0) -
                C2 * PowerHelper(x1)
            ) * totalBalance / scaledInputAmount;
            return inputAmount * BigInteger.Min(amountMultiplier, One) / One;
        }

//
//     /*
//      * Equilibrium is when ratio of amounts equals to ratio of balances
//      *
//      *  x      xBalance
//      * --- == ----------
//      *  y      yBalance
//      *
//      */
        BigInteger _checkVirtualAmountsFormula(BigInteger x, BigInteger y, BigInteger xBalance, BigInteger yBalance)
        {
            return (x * yBalance - y * xBalance);
        }

//
//     /*
//      * Initial approximation of dx is taken from the same equation by assuming dx ~ dy
//      *
//      * x - dx     xBalance + dx
//      * ------  =  ------------
//      * y + dx     yBalance - dx
//      *
//      * dx = (x * yBalance - xBalance * y) / (xBalance + yBalance + x + y)
//      *
//      */
        (BigInteger, BigInteger) _getVirtualAmountsForDeposit(BigInteger x, BigInteger y, BigInteger xBalance,
            BigInteger yBalance)
        {
            BigInteger dx = (x * yBalance - y * xBalance) / (xBalance + yBalance + x + y);
            if (dx == 0)
            {
                return (x, y);
            }

            BigInteger left = dx * 998 / 1000;
            BigInteger right = BigInteger.Min(dx * 1002 / 1000, yBalance);
            BigInteger dy = _getReturn(xBalance, yBalance, dx);
            BigInteger shift = _checkVirtualAmountsFormula(x - dx, y + dy, xBalance + dx, yBalance - dy);

            while (left + _threshold < right)
            {
                if (shift > 0)
                {
                    left = dx;
                    dx = (dx + right) / 2;
                }
                else if (shift < 0)
                {
                    right = dx;
                    dx = (left + dx) / 2;
                }
                else
                {
                    break;
                }

                dy = _getReturn(xBalance, yBalance, dx);
                shift = _checkVirtualAmountsFormula(x - dx, y + dy, xBalance + dx, yBalance - dy);
            }

            return (x - dx, y + dy);
        }

//
//     /*
//      * Initial approximation of dx is taken from the same equation by assuming dx ~ dy
//      *
//      * x - dx        firstTokenShare
//      * ------  =  ----------------------
//      * y + dx     _ONE - firstTokenShare
//      *
//      * dx = (x * (_ONE - firstTokenShare) - y * firstTokenShare) / _ONE
//      */
//
        (BigInteger, BigInteger) _getRealAmountsForWithdraw(BigInteger virtualX, BigInteger virtualY,
            BigInteger balanceX, BigInteger balanceY, BigInteger firstTokenShare)
        {
            Require(balanceX != 0 || balanceY != 0, "Amount exceeds total balance");
            if (firstTokenShare == 0)
            {
                return (0, virtualY + _getReturn(balanceX, balanceY, virtualX));
            }

            BigInteger secondTokenShare = One - firstTokenShare;
            BigInteger dx = (virtualX * (One - firstTokenShare) - virtualY * firstTokenShare) / One;
            BigInteger left = dx * 998 / 1000;
            BigInteger right = BigInteger.Min(dx * 1002 / 1000, balanceY);
            BigInteger dy = _getReturn(balanceX, balanceY, dx);
            BigInteger shift =
                _checkVirtualAmountsFormula(virtualX - dx, virtualY + dy, firstTokenShare, secondTokenShare);

            while (left + _threshold < right)
            {
                if (shift > 0)
                {
                    left = dx;
                    dx = (dx + right) / 2;
                }
                else if (shift < 0)
                {
                    right = dx;
                    dx = (left + dx) / 2;
                }
                else
                {
                    break;
                }

                dy = _getReturn(balanceX, balanceY, dx);
                shift = _checkVirtualAmountsFormula(virtualX - dx, virtualY + dy, firstTokenShare, secondTokenShare);
            }

            return (virtualX - dx, virtualY + dy);
        }

        (BigInteger token0VirtualAmount, BigInteger token1VirtualAmount) GetVirtualAmountsForDeposit(
            BigInteger token0Amount, BigInteger token1Amount)
        {
            BigInteger token0Balance = _token0.BalanceOf((this));
            BigInteger token1Balance = _token1.BalanceOf((this));

            BigInteger shift = _checkVirtualAmountsFormula(token0Amount, token1Amount, token0Balance, token1Balance);
            BigInteger token0VirtualAmount;
            BigInteger token1VirtualAmount;
            if (shift > 0)
            {
                (token0VirtualAmount, token1VirtualAmount) =
                    _getVirtualAmountsForDeposit(token0Amount, token1Amount, token0Balance, token1Balance);
            }
            else if (shift < 0)
            {
                (token1VirtualAmount, token0VirtualAmount) =
                    _getVirtualAmountsForDeposit(token1Amount, token0Amount, token1Balance, token0Balance);
            }
            else
            {
                (token0VirtualAmount, token1VirtualAmount) = (token0Amount, token1Amount);
            }

            return (token0VirtualAmount, token1VirtualAmount);
        }


        (BigInteger token0RealAmount, BigInteger token1RealAmount) GetRealAmountsForWithdraw(
            BigInteger token0VirtualAmount, BigInteger token1VirtualAmount, BigInteger token0Balance,
            BigInteger token1Balance, BigInteger firstTokenShare)
        {
            BigInteger currentToken0Share = token0VirtualAmount * One / (token0VirtualAmount + token1VirtualAmount);
            BigInteger token0RealAmount;
            BigInteger token1RealAmount;
            if (firstTokenShare < currentToken0Share)
            {
                (token0RealAmount, token1RealAmount) = _getRealAmountsForWithdraw(token0VirtualAmount,
                    token1VirtualAmount, token0Balance - token0VirtualAmount, token1Balance - token1VirtualAmount,
                    firstTokenShare);
            }
            else if (firstTokenShare > currentToken0Share)
            {
                (token1RealAmount, token0RealAmount) = _getRealAmountsForWithdraw(token1VirtualAmount,
                    token0VirtualAmount, token1Balance - token1VirtualAmount, token0Balance - token0VirtualAmount,
                    One - firstTokenShare);
            }
            else
            {
                (token0RealAmount, token1RealAmount) = (token0VirtualAmount, token1VirtualAmount);
            }

            return (token0RealAmount, token1RealAmount);
        }

        public BigInteger Deposit(BigInteger token0Amount, BigInteger token1Amount)
        {
            return DepositFor(token0Amount, token1Amount, _msgSender);
        }

        public BigInteger DepositFor(BigInteger token0Amount, BigInteger token1Amount, object to)
        {
            BigInteger share;
            (BigInteger token0VirtualAmount, BigInteger token1VirtualAmount) =
                GetVirtualAmountsForDeposit(token0Amount, token1Amount);

            BigInteger inputAmount = token0VirtualAmount + token1VirtualAmount;
            Require(inputAmount > 0, "Empty deposit is not allowed");
            Require(to != (this), "Deposit to this is forbidden");
            // _mint also checks require(to != null)

            BigInteger totalSupply = TotalSupply();
            if (totalSupply > 0)
            {
                BigInteger totalBalance = _token0.BalanceOf((this)) + _token1.BalanceOf((this)) +
                    token0Amount + token1Amount - inputAmount;
                share = inputAmount * totalSupply / totalBalance;
            }
            else
            {
                share = inputAmount;
            }

            if (token0Amount > 0)
            {
                _token0.SafeTransferFrom(_msgSender, (this), token0Amount);
            }

            if (token1Amount > 0)
            {
                _token1.SafeTransferFrom(_msgSender, (this), token1Amount);
            }

            Mint(to, share);
            return share;
        }

        public (BigInteger token0Amount, BigInteger token1Amount) Withdraw(BigInteger amount)
        {
            return WithdrawFor(amount, _msgSender);
        }

        public(BigInteger token0Amount, BigInteger token1Amount) WithdrawFor(BigInteger amount, object to)
        {
            Require(amount > 0, "Empty withdrawal is not allowed");
            Require(to != (this), "Withdrawal to this is forbidden");
            Require(to != null, "Withdrawal to zero is forbidden");

            BigInteger totalSupply = TotalSupply();
            var token0Amount = _token0.BalanceOf((this)) * amount / totalSupply;
            var token1Amount = _token1.BalanceOf((this)) * amount / totalSupply;

            Burn(_msgSender, amount);
            if (token0Amount > 0)
            {
                _token0.SafeTransferFrom(this, to, token0Amount);
            }

            if (token1Amount > 0)
            {
                _token1.SafeTransferFrom(this, to, token1Amount);
            }

            return (token0Amount, token1Amount);
        }

        public (BigInteger token0Amount, BigInteger token1Amount) WithdrawWithRatio(BigInteger amount,
            BigInteger firstTokenShare)
        {
            return WithdrawForWithRatio(amount, _msgSender, firstTokenShare);
        }

        public (BigInteger token0Amount, BigInteger token1Amount) WithdrawForWithRatio(BigInteger amount, object to,
            BigInteger firstTokenShare)
        {
            Require(amount > 0, "Empty withdrawal is not allowed");
            Require(to != (this), "Withdrawal to this is forbidden");
            Require(to != null, "Withdrawal to zero is forbidden");
            Require(firstTokenShare <= One, "Ratio should be in [0, 1]");

            BigInteger totalSupply = TotalSupply();
            BigInteger token0Balance = _token0.BalanceOf((this));
            BigInteger token1Balance = _token1.BalanceOf((this));
            BigInteger token0VirtualAmount = token0Balance * amount / totalSupply;
            BigInteger token1VirtualAmount = token1Balance * amount / totalSupply;
            var (token0Amount, token1Amount) = GetRealAmountsForWithdraw(token0VirtualAmount, token1VirtualAmount,
                token0Balance, token1Balance, firstTokenShare);

            Burn(_msgSender, amount);

            if (token0Amount > 0)
            {
                _token0.SafeTransferFrom(this, to, token0Amount);
            }

            if (token1Amount > 0)
            {
                _token1.SafeTransferFrom(this, to, token1Amount);
            }

            return (token0Amount, token1Amount);
        }

        public BigInteger Swap0To1(BigInteger inputAmount)
        {
            return _swap(_token0, _token1, inputAmount, _msgSender);
        }

        public BigInteger Swap1To0(BigInteger inputAmount)
        {
            return _swap(_token1, _token0, inputAmount, _msgSender);
        }

        public BigInteger Swap0To1For(BigInteger inputAmount, object to)
        {
            Require(to != (this), "Swap to this is forbidden");
            Require(to != null, "Swap to zero is forbidden");

            return _swap(_token0, _token1, inputAmount, to);
        }

        public BigInteger Swap1To0For(BigInteger inputAmount, object to)
        {
            Require(to != (this), "Swap to this is forbidden");
            Require(to != null, "Swap to zero is forbidden");

            return _swap(_token1, _token0, inputAmount, to);
        }

        BigInteger _swap(Erc20 tokenFrom, Erc20 tokenTo, BigInteger inputAmount, object to)
        {
            Require(inputAmount > 0, "Input amount should be > 0");
            var outputAmount = GetReturn(tokenFrom, tokenTo, inputAmount);
            Require(outputAmount > 0, "Empty swap is not allowed");
            tokenFrom.SafeTransferFrom(_msgSender, (this), inputAmount);
            tokenTo.SafeTransferFrom(this, to, outputAmount);
            return outputAmount;
        }

        private static BigInteger PowerHelper(BigInteger x)
        {
            BigInteger p;
            if (x > C3)
            {
                p = x - C3;
            }
            else
            {
                p = C3 - x;
            }

            p = p * p / One; // p ^ 2
            var pp = p * p / One; // p ^ 4
            pp = pp * pp / One; // p ^ 8
            pp = pp * pp / One; // p ^ 16
            p = p * pp / One; // p ^ 18
            return p;
        }

        private static void Require(bool condition, string message = null)
        {
            if (!condition) throw new InvalidOperationException(message ?? "Require failed");
        }
    }
}