using System;
using System.Collections.Generic;
using MathWorks.MATLAB.NET.Arrays;
using MathWorks.MATLAB.NET.Utility;
using NNeuroPitLib;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.DataSource;
using TSLab.Script.Helpers;
using System.IO;




namespace NNNeuroPit
{

    [HandlerCategory("NN")]
    [HandlerName("NN NeuroPit")]


    public class NNNeuroPitClass : IBar2BoolsHandler
    {

        [HandlerParameter(Name = "ТолькоНаправление", Default = "0", NotOptimized = true)]
        public string OnlySellBuy { get; set; }

        [HandlerParameter(Name = "Усиление", Default = "0", NotOptimized = false)]
        public double ToAdd { get; set; }

        public class nBar
        {

            public DateTime Date;
            public double Open;
            public double Close;
            public double Hight;
            public double Low;
            public double Vol;
        }

        public class ExtBar
        {

            public DateTime Date;
            public double Open;
            public double Close;
            public double Hight;
            public double Low;
            public double Vol;
            public bool HourKick;
            public bool DayKick;
            public int DayKickNumber;
            public bool ThreeDayKick;
            public bool MonthKick;
            public bool isSell;
            public double VolHour;
            public double VolDay;
            public double BarDelta;
            public bool IsTrendContinue;
        }

        static string NumberFormat(string Number)
        {
            return Number.Replace(",", ".");
        }

        static double GetMinValue(IList<Bar> Bars, int Current, int Count)
        {
            double MinVal = 9999999;
            for (int i = 1; i < Count; i++)
            {
                if (Current - i < 0)
                {
                    if (Current == 0)
                    {
                        MinVal = Bars[0].Close;
                    }
                    break;
                }
                if (Bars[Current - i].Close < MinVal)
                {
                    MinVal = Bars[Current - i].Close;
                }
            }
            return MinVal;
        }

        static double GetMaxValue(IList<Bar> Bars, int Current, int Count)
        {

            double MaxVal = 0;
            for (int i = 1; i < Count; i++)
            {
                if (Current - i < 0)
                {
                    if (Current == 0)
                    {
                        MaxVal = Bars[0].Close;
                    }
                    break;
                }
                if (Bars[Current - i].Close > MaxVal)
                {
                    MaxVal = Bars[Current - i].Close;
                }
            }
            return MaxVal;
        }

        static double CalculateVol(IList<Bar> Bars, int Current, int Count)
        {
            // Вычисляем среднее
            double closesum = 0;
            double mo = 0;
            if (Current + Count > Bars.Count)
            {
                Count = Bars.Count - Current;
            }
            for (int i = Current; i < Current + Count; i++)
            {
                closesum = closesum + Bars[i].Close;
            }
            mo = closesum / Count;

            double dispsum = 0;
            // Теперь суммируем квадраты разности для дисперсии
            for (int i = Current; i < Current + Count; i++)
            {
                dispsum = dispsum + Math.Pow(Bars[i].Close - mo, 2);
            }
            return Math.Round(Math.Sqrt(dispsum / (Count - 1)), 4);
        }

        // Функиция по трендам раскладывает пробития за день и считает число пробитий
        //
        static void CalculateKicks(IList<ExtBar> Bars)
        {

            for (int Barindex = 1; Barindex < Bars.Count - 1; Barindex++)
            {
                int CurrendDay = Bars[Barindex].Date.Day;
                int BuyKickNumber = 0;
                int SellKickNUmber = 0;
                bool CurrentCounted = false;
                int StartDayIndex = Barindex;

                while (CurrendDay == Bars[Barindex].Date.Day && Barindex < Bars.Count - 1)
                {
                    if (Bars[Barindex].DayKick)
                    {
                        if (BuyKickNumber == 0 && SellKickNUmber == 0) //Первый раз
                        {
                            if (Bars[Barindex].isSell)
                            {
                                SellKickNUmber++;
                            }
                            else
                            {
                                BuyKickNumber++;
                            }
                            CurrentCounted = true;
                        }

                        else
                        {
                            if (CurrentCounted) //Продолжение ряда
                            {
                                Barindex++;
                                if (Bars[Barindex].isSell != Bars[Barindex - 1].isSell) // При переходе от покупке к продаже и обратно сбрасываем продолжение ряда
                                {
                                    CurrentCounted = false;
                                }
                                continue;
                            }
                            else
                            {
                                if (Bars[Barindex].isSell)
                                {
                                    SellKickNUmber++;
                                }
                                else
                                {
                                    BuyKickNumber++;
                                }
                            }
                        }
                    }

                    if (Bars[Barindex].isSell)
                    {
                        Bars[Barindex].DayKickNumber = SellKickNUmber;
                    }
                    else
                    {
                        Bars[Barindex].DayKickNumber = BuyKickNumber;
                    }

                    Barindex++;
                    if (Bars[Barindex].isSell != Bars[Barindex - 1].isSell) // При переходе от покупке к продаже и обратно сбрасываем продолжение ряда
                    {
                        CurrentCounted = false;
                    }
                }

            }
        }
    

        public IContext Context { set; private get; }

        public IList<bool> Execute(ISecurity source)
        {

            int ValCount = source.Bars.Count;
            int Lines = ValCount;
            int SymbolsCount = 18;
            var values = new bool[ValCount];
            NNClass NNMatlab = new NNClass();
            MWNumericArray NNres;
            MWNumericArray x2 = new double[SymbolsCount];
            double NNtotal;
            IList<ExtBar> ExtBars = new List<ExtBar>();


            int HourBarsCount = 60;
            int DayBarsCount = HourBarsCount * 14;
            int ThreeDayBarsCount = DayBarsCount * 3;
            int MonthBarsCount = DayBarsCount * 30;
            var Bars = source.Bars;
            int BarIndex = 0;
            int DeltaKick = 0; 

            double MinHour = 0;
            double MinDay = 0;
            double MinThreeDay = 0;
            double MinMonth = 0;

            double MaxHour = 0;
            double MaxDay = 0;
            double MaxThreeDay = 0;
            double MaxMonth = 0;
            double AddValue = 0;
            DateTime PrevDate = Bars[0].Date.Date;

            AddValue = ToAdd;

            foreach (Bar oBar in Bars)
            {
                double CurrentPrice = oBar.Close;
                DateTime barDate = oBar.Date.Date;

                MinHour = GetMinValue(Bars, BarIndex, HourBarsCount);
                MinDay = GetMinValue(Bars, BarIndex, DayBarsCount);
                MinThreeDay = GetMinValue(Bars, BarIndex, ThreeDayBarsCount);
                MinMonth = GetMinValue(Bars, BarIndex, MonthBarsCount);

                MaxHour = GetMaxValue(Bars, BarIndex, HourBarsCount);
                MaxDay = GetMaxValue(Bars, BarIndex, DayBarsCount);
                MaxThreeDay = GetMaxValue(Bars, BarIndex, ThreeDayBarsCount);
                MaxMonth = GetMaxValue(Bars, BarIndex, MonthBarsCount);

                ExtBar NewExtBar = new ExtBar();
                NewExtBar.Close = oBar.Close;
                NewExtBar.Date = oBar.Date;
                NewExtBar.Open = oBar.Open;
                NewExtBar.Hight = oBar.High;
                NewExtBar.Low = oBar.Low;
                NewExtBar.Vol = oBar.Volume;

                if (oBar.Close < (MinHour - DeltaKick))
                {
                    NewExtBar.HourKick = true;
                    NewExtBar.isSell = true;
                }

                if (oBar.Close < (MinDay - DeltaKick))
                {
                    NewExtBar.DayKick = true;
                    NewExtBar.isSell = true;
                }

                if (oBar.Close < (MinThreeDay - DeltaKick))
                {
                    NewExtBar.ThreeDayKick = true;
                    NewExtBar.isSell = true;
                }

                if (oBar.Close < (MinMonth - DeltaKick))
                {
                    NewExtBar.MonthKick = true;
                    NewExtBar.isSell = true;
                }

                if (oBar.Close > (MaxHour + DeltaKick))
                {
                    NewExtBar.HourKick = true;
                    NewExtBar.isSell = false;
                }

                if (oBar.Close > (MaxDay + DeltaKick))
                {
                    NewExtBar.DayKick = true;
                    NewExtBar.isSell = false;
                }

                if (oBar.Close > (MaxThreeDay + DeltaKick))
                {
                    NewExtBar.ThreeDayKick = true;
                    NewExtBar.isSell = false;
                }

                if (oBar.Close > (MaxMonth + DeltaKick))
                {
                    NewExtBar.MonthKick = true;
                    NewExtBar.isSell = false;
                }

                if (NewExtBar.HourKick)
                {
                    NewExtBar.VolDay = CalculateVol(Bars, BarIndex, DayBarsCount);
                    NewExtBar.VolHour = CalculateVol(Bars, BarIndex, HourBarsCount);

                    if (!NewExtBar.isSell)
                    {
                        NewExtBar.BarDelta = NewExtBar.Close - NewExtBar.Open;
                    }
                    else
                    {
                        NewExtBar.BarDelta = NewExtBar.Open - NewExtBar.Close;
                    }
                    
                }

                ExtBars.Add(NewExtBar);

                BarIndex++;
            }

            CalculateKicks(ExtBars); 



            for (var i = 0; i < ValCount-1; i++)
            {
                MWArray result;

                ExtBar newbar = ExtBars[i];
                x2[1] = (double)newbar.Date.Month;
                x2[2] = (double)newbar.Date.Day;
                x2[3] = (double)newbar.Date.DayOfWeek;
                x2[4] = (double)newbar.Date.Hour;
                x2[5] = (double)((newbar.MonthKick) ? 1 : 0);
                x2[6] = (double)((newbar.ThreeDayKick) ? 1 : 0);
                x2[7] = (double)((newbar.DayKick) ? 1 : 0);
                x2[8] = (double)((newbar.HourKick) ? 1 : 0);
                x2[9] = (double)newbar.DayKickNumber;
                x2[10] = (double)newbar.BarDelta;
                x2[11] = (double)newbar.Open;
                x2[12] = (double)newbar.Hight;
                x2[13] = (double)newbar.Low;
                x2[14] = (double)newbar.Close;
                x2[15] = (double)newbar.Vol;
                x2[16] = (double)newbar.VolHour;
                x2[17] = (double)newbar.VolDay;
                x2[18] = (double)((newbar.isSell) ? 1 : 0);

                if (OnlySellBuy == "1")
                {
                    values[i] = newbar.isSell;
                }
                else if (!newbar.HourKick)
                {
                    values[i] = false;
                }
                else
                {
                    result = NNMatlab.NNFunc(x2);
                    NNres = (MWNumericArray)result[1];
                    NNtotal = (double)NNres[1];
                    int iRez = Convert.ToInt32(Math.Round(NNtotal + AddValue));
                    if (iRez > 0)
                    {
                        values[i] = true;
                    }
                    else
                    {
                        values[i] = false;
                    }
                }               
                    
            }

            return values;
        }

    }
}
