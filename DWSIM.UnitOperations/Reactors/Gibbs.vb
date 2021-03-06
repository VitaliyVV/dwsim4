'    Gibbs Reactor Calculation Routines 
'    Copyright 2008-2010 Daniel Wagner O. de Medeiros
'
'    This file is part of DWSIM.
'
'    DWSIM is free software: you can redistribute it and/or modify
'    it under the terms of the GNU General Public License as published by
'    the Free Software Foundation, either version 3 of the License, or
'    (at your option) any later version.
'
'    DWSIM is distributed in the hope that it will be useful,
'    but WITHOUT ANY WARRANTY; without even the implied warranty of
'    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
'    GNU General Public License for more details.
'
'    You should have received a copy of the GNU General Public License
'    along with DWSIM.  If not, see <http://www.gnu.org/licenses/>.

Imports DWSIM.DrawingTools.GraphicObjects
Imports DWSIM.Thermodynamics.BaseClasses
Imports Ciloci.Flee
Imports System.Math
Imports System.Linq
Imports DWSIM.MathOps.MathEx.Common
Imports DotNumerics.Optimization
Imports DWSIM.MathOps.MathEx
Imports DWSIM.Interfaces.Enums
Imports DWSIM.SharedClasses
Imports DWSIM.Thermodynamics.Streams
Imports DWSIM.Thermodynamics
Imports DWSIM.MathOps

Namespace Reactors

    <System.Serializable()> Public Class Reactor_Gibbs

        Inherits Reactor

        <NonSerialized> <Xml.Serialization.XmlIgnore> Dim f As EditingForm_ReactorConvEqGibbs

        Public Enum SolvingMethod

            ReactionExtents = 0
            DirectMinimization = 1

        End Enum

        Private _solvemethod As SolvingMethod = SolvingMethod.DirectMinimization
        Protected m_reactionextents As Dictionary(Of String, Double)
        Private _rex_iest As New ArrayList
        Private _el_mat As Double(,) = New Double(,) {}
        Private _components As New List(Of String)
        Private _initialestimates As New List(Of Double)
        Private _elements As String() = {}
        Private _totalelements As Double() = {}
        Private _ige, _fge, _elbal As Double

        Dim tmpx As Double(), tmpdx As Double()

        Dim tms As MaterialStream
        Dim N0 As New Dictionary(Of String, Double)
        Dim DN As New Dictionary(Of String, Double)
        Dim N As New Dictionary(Of String, Double)
        Dim T, T0, P, P0, Ninerts, Winerts, E(,) As Double
        Dim r, c, els, comps, cnt As Integer
        Dim ims As MaterialStream

        Public Sub New()
            MyBase.New()
        End Sub

        Public Overrides Function LoadData(data As System.Collections.Generic.List(Of System.Xml.Linq.XElement)) As Boolean

            MyBase.LoadData(data)

            Dim ci As Globalization.CultureInfo = Globalization.CultureInfo.InvariantCulture

            For Each xel2 As XElement In (From xel As XElement In data Select xel Where xel.Name = "Compounds").Elements
                _components.Add(xel2.@ID)
            Next

            For Each xel2 As XElement In (From xel As XElement In data Select xel Where xel.Name = "InitialEstimates").Elements
                _initialestimates.Add(Double.Parse(xel2.@Value, ci))
            Next

            Dim elmns As New ArrayList
            For Each xel2 As XElement In (From xel As XElement In data Select xel Where xel.Name = "Elements").Elements
                elmns.Add(xel2.@ID)
            Next
            _elements = elmns.ToArray(Type.GetType("System.String"))

            Dim telmns As New ArrayList
            For Each xel2 As XElement In (From xel As XElement In data Select xel Where xel.Name = "TotalElements").Elements
                telmns.Add(Double.Parse(xel2.@ID, ci))
            Next
            _totalelements = telmns.ToArray(Type.GetType("System.Double"))

            Dim elmattext As String = (From xel As XElement In data Select xel Where xel.Name = "ElementMatrix").SingleOrDefault.Value

            Dim rows() As String = elmattext.Split(":")
            Dim n As Integer = rows.Length - 1
            Dim m As Integer = 0
            If n > 0 Then
                m = rows(0).Split(",").Length - 1
            End If
            If n > 0 And m > 0 Then
                Dim elm(n, m) As Double
                For i As Integer = 0 To n
                    For j As Integer = 0 To m
                        elm(i, j) = Double.Parse(rows(i).Split(",")(j))
                    Next
                Next
                _el_mat = elm
            End If
            Return True
        End Function

        Public Overrides Function SaveData() As System.Collections.Generic.List(Of System.Xml.Linq.XElement)

            Dim elements As System.Collections.Generic.List(Of System.Xml.Linq.XElement) = MyBase.SaveData()
            Dim ci As Globalization.CultureInfo = Globalization.CultureInfo.InvariantCulture

            With elements
                .Add(New XElement("Compounds"))
                For Each s As String In _components
                    .Item(.Count - 1).Add(New XElement("Compound", New XAttribute("ID", s)))
                Next
                .Add(New XElement("InitialEstimates"))
                For Each d As Double In _initialestimates
                    .Item(.Count - 1).Add(New XElement("Item", New XAttribute("Value", d.ToString(ci))))
                Next
                .Add(New XElement("Elements"))
                For Each s As String In _elements
                    .Item(.Count - 1).Add(New XElement("Element", New XAttribute("ID", s)))
                Next
                .Add(New XElement("TotalElements"))
                For Each d As Double In _totalelements
                    .Item(.Count - 1).Add(New XElement("Element", New XAttribute("ID", d.ToString(ci))))
                Next
                Dim elmattext As String = ""
                For i As Integer = 0 To _el_mat.GetUpperBound(0)
                    For j As Integer = 0 To _el_mat.GetUpperBound(1)
                        elmattext += _el_mat(i, j).ToString(ci) + ","
                    Next
                    elmattext = elmattext.TrimEnd(",") + ":"
                Next
                elmattext = elmattext.TrimEnd(":")
                .Add(New XElement("ElementMatrix", elmattext))
            End With

            Return elements

        End Function

#Region "Properties"

        Public Property InitialGibbsEnergy() As Double
            Get
                Return _ige
            End Get
            Set(ByVal value As Double)
                _ige = value
            End Set
        End Property

        Public Property FinalGibbsEnergy() As Double
            Get
                Return _fge
            End Get
            Set(ByVal value As Double)
                _fge = value
            End Set
        End Property

        Public ReadOnly Property ElementBalance() As Double
            Get
                Return _elbal
            End Get
        End Property

        Public ReadOnly Property ReactionExtents() As Dictionary(Of String, Double)
            Get
                Return Me.m_reactionextents
            End Get
        End Property

        Public ReadOnly Property ReactionExtentsEstimates() As ArrayList
            Get
                Return _rex_iest
            End Get
        End Property

        Public Property Elements() As String()
            Get
                Return _elements
            End Get
            Set(ByVal value As String())
                _elements = value
            End Set
        End Property

        Public Property ComponentIDs() As List(Of String)
            Set(value As List(Of String))
                _components = value
            End Set
            Get
                Return _components
            End Get
        End Property

        Public Property InitialEstimates() As List(Of Double)
            Set(value As List(Of Double))
                _initialestimates = value
            End Set
            Get
                If _initialestimates Is Nothing Then _initialestimates = New List(Of Double)
                Return _initialestimates
            End Get
        End Property

        Public Property ElementMatrix() As Double(,)
            Get
                Return _el_mat
            End Get
            Set(ByVal value As Double(,))
                _el_mat = value
            End Set
        End Property

        Public Property TotalElements() As Double()
            Get
                Return _totalelements
            End Get
            Set(ByVal value As Double())
                _totalelements = value
            End Set
        End Property

        Public Property SolvMethod() As SolvingMethod
            Get
                Return _solvemethod
            End Get
            Set(ByVal value As SolvingMethod)
                _solvemethod = value
            End Set
        End Property

#End Region

#Region "Auxiliary Functions"

        Private Function FunctionGradient(ByVal x() As Double) As Double()

            Dim epsilon As Double = 0.0001

            Dim f1, f2 As Double
            Dim g(x.Length - 1), x2(x.Length - 1) As Double
            Dim i, j As Integer

            For i = 0 To x.Length - 1
                f1 = FunctionValue(x)
                For j = 0 To x.Length - 1
                    If x(j) = 0 Then
                        If i <> j Then
                            x2(j) = (x(j) + 0.000001)
                        Else
                            x2(j) = (x(j) + 0.000001) * (1 + epsilon)
                        End If
                    Else
                        If i <> j Then
                            x2(j) = x(j)
                        Else
                            x2(j) = x(j) * (1 + epsilon)
                        End If
                    End If
                Next
                f2 = FunctionValue(x2)
                g(i) = (f2 - f1) / (x2(i) - x(i))
            Next

            Return g

        End Function

        Private Function FunctionGradient2N(ByVal x() As Double) As Double(,)

            Dim epsilon As Double = 0.0001

            Dim f1(), f2() As Double
            Dim g(x.Length - 1, x.Length - 1), x2(x.Length - 1) As Double
            Dim i, j, k As Integer

            f1 = FunctionValue2N(x)
            For i = 0 To x.Length - 1
                For j = 0 To x.Length - 1
                    If i <> j Then
                        x2(j) = x(j)
                    Else
                        x2(j) = x(j) * (1 + epsilon)
                    End If
                Next
                f2 = FunctionValue2N(x2)
                For k = 0 To x.Length - 1
                    g(k, i) = (f2(k) - f1(k)) / (x2(i) - x(i))
                Next
            Next

            Return g

        End Function

        Private Function FunctionValue(ByVal x() As Double) As Double

            tms = ims.Clone()
            tms.SetFlowsheet(ims.FlowSheet)

            Dim i As Integer

            Dim pp As PropertyPackages.PropertyPackage = Me.PropertyPackage

            cnt += 1

            i = 0
            For Each s As String In N.Keys
                DN(s) = 0
                For j = 0 To r
                    DN(s) += E(i, j) * x(j)
                Next
                i += 1
            Next

            For Each s As String In DN.Keys
                N(s) = N0(s) + DN(s)
                'If N(s) > -0.1 And N(s) < 0 Then N(s) = 0.0#
            Next

            Dim fw(comps), fm(comps), sumfm, sum1, sumn, sumw As Double

            N.Values.CopyTo(fm, 0)

            sumfm = Sum(fm) + Ninerts

            sum1 = 0
            sumn = 0
            For Each s As Compound In tms.Phases(0).Compounds.Values
                If Me.ComponentIDs.Contains(s.Name) Then
                    s.MolarFlow = N(s.Name)
                    s.MoleFraction = N(s.Name) / sumfm
                    sum1 += N(s.Name) * s.ConstantProperties.Molar_Weight / 1000
                Else
                    s.MoleFraction = s.MolarFlow / sumfm
                End If
                sumn += s.MolarFlow
            Next

            tms.Phases(0).Properties.molarflow = sumn

            sumw = 0
            For Each s As Compound In tms.Phases(0).Compounds.Values
                If Me.ComponentIDs.Contains(s.Name) Then
                    s.MassFlow = N(s.Name) * s.ConstantProperties.Molar_Weight / 1000
                End If
                s.MassFraction = s.MassFlow / (sum1 + Winerts)
                sumw += s.MassFlow
            Next

            tms.Phases(0).Properties.massflow = sumw

            With pp
                .CurrentMaterialStream = tms
                .DW_CalcEquilibrium(PropertyPackages.FlashSpec.T, PropertyPackages.FlashSpec.P)
                .DW_CalcPhaseProps(PropertyPackages.Phase.Mixture)
                .DW_CalcPhaseProps(PropertyPackages.Phase.Vapor)
                .DW_CalcPhaseProps(PropertyPackages.Phase.Liquid)
                .DW_CalcCompMolarFlow(-1)
                .DW_CalcCompMassFlow(-1)
                .DW_CalcCompVolFlow(-1)
                .DW_CalcOverallProps()
                .DW_CalcTwoPhaseProps(PropertyPackages.Phase.Liquid, PropertyPackages.Phase.Vapor)
                .DW_CalcVazaoVolumetrica()
                .DW_CalcKvalue()
            End With

            Dim fugs(tms.Phases(0).Compounds.Count - 1) As Double
            Dim CP(tms.Phases(0).Compounds.Count - 1) As Double
            Dim DGf As Double

            i = 0
            For Each s As Compound In tms.Phases(2).Compounds.Values
                If s.MoleFraction <> 0.0# Then
                    DGf = pp.AUX_DELGF_T(298.15, T, s.Name) * s.ConstantProperties.Molar_Weight
                    fugs(i) = s.FugacityCoeff.GetValueOrDefault
                    'XC = s.MoleFraction.GetValueOrDefault
                    If s.MoleFraction.GetValueOrDefault <= 0 Then
                        'CP(i) = s.MoleFraction * DGf * 10
                        CP(i) = s.MoleFraction * -200
                    Else
                        CP(i) = s.MoleFraction * (DGf + Log(fugs(i) * s.MoleFraction * P / P0))
                    End If
                Else
                    CP(i) = 0
                End If
                i += 1
            Next

            Dim pen_val As Double = ReturnPenaltyValue()

            Dim gibbs As Double = MathEx.Common.Sum(CP) * sumn * 8.314 * T

            If Double.IsNaN(gibbs) Or Double.IsInfinity(gibbs) Then
                Return pen_val
            Else
                Return gibbs + pen_val
            End If


        End Function

        Private Function FunctionValue2N(ByVal x() As Double) As Double()

            Dim i As Integer

            Dim pp As PropertyPackages.PropertyPackage = Me.PropertyPackage

            i = 0
            For Each s As String In N.Keys
                DN(s) = x(i) - N0(s)
                i += 1
            Next

            i = 0
            For Each s As String In DN.Keys
                N(s) = x(i)
                i += 1
            Next

            Dim fw(comps), fm(comps), sumfm, sum1, sumn, sumw As Double

            N.Values.CopyTo(fm, 0)

            sumfm = Sum(fm) + Ninerts

            sum1 = 0
            sumn = 0
            For Each s As Compound In tms.Phases(0).Compounds.Values
                If Me.ComponentIDs.Contains(s.Name) Then
                    s.MolarFlow = N(s.Name)
                    s.MoleFraction = N(s.Name) / sumfm
                    sum1 += N(s.Name) * s.ConstantProperties.Molar_Weight / 1000
                Else
                    s.MoleFraction = s.MolarFlow / sumfm
                End If
                sumn += s.MolarFlow
            Next

            tms.Phases(0).Properties.molarflow = sumn

            sumw = 0
            For Each s As Compound In tms.Phases(0).Compounds.Values
                If Me.ComponentIDs.Contains(s.Name) Then
                    s.MassFlow = N(s.Name) * s.ConstantProperties.Molar_Weight / 1000
                End If
                s.MassFraction = s.MassFlow / (sum1 + Winerts)
                sumw += s.MassFlow
            Next

            tms.Phases(0).Properties.massflow = sumw

            With pp
                .CurrentMaterialStream = tms
                .DW_CalcEquilibrium(PropertyPackages.FlashSpec.T, PropertyPackages.FlashSpec.P)
                .DW_CalcPhaseProps(PropertyPackages.Phase.Mixture)
                .DW_CalcPhaseProps(PropertyPackages.Phase.Vapor)
                .DW_CalcPhaseProps(PropertyPackages.Phase.Liquid)
                .DW_CalcCompMolarFlow(-1)
                .DW_CalcCompMassFlow(-1)
                .DW_CalcCompVolFlow(-1)
                .DW_CalcOverallProps()
                .DW_CalcTwoPhaseProps(PropertyPackages.Phase.Liquid, PropertyPackages.Phase.Vapor)
                .DW_CalcVazaoVolumetrica()
                .DW_CalcKvalue()
            End With

            Dim CP(tms.Phases(0).Compounds.Count - 1) As Double
            Dim f(x.Length - 1) As Double
            Dim DGf As Double

            'CP is the chemical potential

            Dim fugs(tms.Phases(0).Compounds.Count - 1) As Double

            i = 0
            For Each s As Compound In tms.Phases(2).Compounds.Values
                If s.MoleFraction > 0.0# Then
                    DGf = pp.AUX_DELGF_T(298.15, T, s.Name) * s.ConstantProperties.Molar_Weight
                    fugs(i) = s.FugacityCoeff.GetValueOrDefault
                    CP(i) = (DGf + Log(fugs(i) * s.MoleFraction * P / P0))
                Else
                    CP(i) = 0
                End If
                i += 1
            Next

            Dim pen_val As Double = ReturnPenaltyValue()

            Dim gibbs As Double = MathEx.Common.Sum(CP)

            Dim sumel(els), sumeli(comps), totalsum As Double

            totalsum = 0
            For i = 0 To els
                sumel(i) = 0
                For j = 0 To comps
                    sumel(i) += Me.ElementMatrix(i, j) * x(j)
                Next
                'sumel(i) -= Me.TotalElements(i)
                'sumel(i) *= x(comps + i + 1)
                totalsum += sumel(i)
            Next

            For j = 0 To comps
                sumeli(j) = 0
                For i = 0 To els
                    sumeli(j) += Me.ElementMatrix(i, j) * x(comps + i + 1)
                Next
            Next

            For i = 0 To x.Length - 1
                If i <= comps Then
                    f(i) = CP(i) - sumeli(i) + pen_val
                Else
                    f(i) = -sumel(i - comps - 1) + Me.TotalElements(i - comps - 1)
                End If
            Next

            Return f

        End Function

        Public Function MinimizeError(ByVal t As Double) As Double

            Dim tmpx0 As Double() = tmpx.Clone

            For i = 0 To comps + els + 1
                tmpx0(i) -= tmpdx(i) * t
                'If tmpx0(i) < 0 And i <= comps Then tmpx0(i) = 0.000001
            Next

            Dim abssum0 = AbsSum(FunctionValue2N(tmpx0))
            Return abssum0

        End Function

        Private Function FunctionValue2G(ByVal x() As Double) As Double

            Dim i As Integer

            Dim pp As PropertyPackages.PropertyPackage = Me.PropertyPackage

            i = 0
            For Each s As String In N.Keys
                DN(s) = x(i) - N0(s)
                i += 1
            Next

            i = 0
            For Each s As String In DN.Keys
                N(s) = x(i)
                If x(i) < 0 Then N(s) = 0
                i += 1
            Next

            Dim fw(comps), fm(comps), sumfm, sum1, sumn, sumw As Double

            N.Values.CopyTo(fm, 0)

            sumfm = Sum(fm) + Ninerts

            sum1 = 0
            sumn = 0
            For Each s As Compound In tms.Phases(0).Compounds.Values
                If Me.ComponentIDs.Contains(s.Name) Then
                    s.MolarFlow = N(s.Name)
                    s.MoleFraction = N(s.Name) / sumfm
                    sum1 += N(s.Name) * s.ConstantProperties.Molar_Weight / 1000
                Else
                    s.MoleFraction = s.MolarFlow / sumfm
                End If
                sumn += s.MolarFlow
            Next

            tms.Phases(0).Properties.molarflow = sumn

            sumw = 0
            For Each s As Compound In tms.Phases(0).Compounds.Values
                If Me.ComponentIDs.Contains(s.Name) Then
                    s.MassFlow = N(s.Name) * s.ConstantProperties.Molar_Weight / 1000
                End If
                s.MassFraction = s.MassFlow / (sum1 + Winerts)
                sumw += s.MassFlow
            Next

            tms.Phases(0).Properties.massflow = sumw

            With pp
                .CurrentMaterialStream = tms
                .DW_CalcEquilibrium(PropertyPackages.FlashSpec.T, PropertyPackages.FlashSpec.P)
                .DW_CalcPhaseProps(PropertyPackages.Phase.Mixture)
                .DW_CalcPhaseProps(PropertyPackages.Phase.Vapor)
                .DW_CalcPhaseProps(PropertyPackages.Phase.Liquid)
                .DW_CalcCompMolarFlow(-1)
                .DW_CalcCompMassFlow(-1)
                .DW_CalcCompVolFlow(-1)
                .DW_CalcOverallProps()
                .DW_CalcTwoPhaseProps(PropertyPackages.Phase.Liquid, PropertyPackages.Phase.Vapor)
                .DW_CalcVazaoVolumetrica()
                .DW_CalcKvalue()
            End With

            Dim fugs(tms.Phases(0).Compounds.Count - 1) As Double
            Dim CP(tms.Phases(0).Compounds.Count - 1) As Double
            Dim DGf As Double

            i = 0
            For Each s As Compound In tms.Phases(2).Compounds.Values
                If s.MoleFraction > 0.0# Then
                    DGf = pp.AUX_DELGF_T(298.15, T, s.Name) * s.ConstantProperties.Molar_Weight
                    fugs(i) = s.FugacityCoeff.GetValueOrDefault
                    CP(i) = s.MoleFraction * (DGf + Log(fugs(i) * s.MoleFraction.GetValueOrDefault * P / P0))
                Else
                    CP(i) = 0
                End If
                i += 1
            Next

            Dim gibbs As Double = MathEx.Common.Sum(CP) * 8.314 * T * sumn

            Return gibbs

        End Function

        Private Function FunctionValue2FC(ByVal x() As Double) As Double

            Dim sumel(els), totalsum As Double

            totalsum = 0
            For i = 0 To els
                sumel(i) = 0
                For j = 0 To comps
                    sumel(i) += Me.ElementMatrix(i, j) * x(j)
                Next
                sumel(i) -= Me.TotalElements(i)
                totalsum += sumel(i)
            Next

            Return totalsum

        End Function

        Private Function ReturnPenaltyValue() As Double

            'calculate penalty functions for constraint variables

            Dim i As Integer
            Dim n As Integer = tms.Phases(0).Compounds.Count - 1

            Dim con_lc(n), con_uc(n), con_val(n) As Double
            Dim pen_val As Double = 0
            Dim delta1, delta2 As Double

            i = 0
            For Each comp As Compound In tms.Phases(0).Compounds.Values
                con_lc(i) = 0
                con_uc(i) = 1
                con_val(i) = comp.MoleFraction.GetValueOrDefault
                i += 1
            Next

            pen_val = 0
            For i = 0 To n
                delta1 = con_val(i) - con_lc(i)
                delta2 = con_val(i) - con_uc(i)
                If delta1 < 0 Then
                    pen_val += -delta1 * 1000000
                ElseIf delta2 > 1 Then
                    pen_val += -delta2 * 1000000
                Else
                    pen_val += 0
                End If
            Next

            If Double.IsNaN(pen_val) Then pen_val = 0

            Return pen_val

        End Function

#End Region

#Region "Auxiliary Subs"

        Public Sub CreateElementMatrix()

            Dim ims As MaterialStream = FlowSheet.SimulationObjects(Me.GraphicObject.InputConnectors(0).AttachedConnector.AttachedFrom.Name)

            N0.Clear()

            For Each cname As String In Me.ComponentIDs
                N0.Add(cname, ims.Phases(0).Compounds(cname).MolarFlow)
            Next

            Dim elements As New List(Of String)

            For Each sn As String In Me.ComponentIDs
                For Each el As String In Me.FlowSheet.SelectedCompounds(sn).Elements.Keys
                    If Not elements.Contains(el) Then elements.Add(el)
                Next
            Next

            Me.Elements = elements.ToArray()

            els = Me.Elements.Length - 1
            c = Me.ComponentIDs.Count - 1

            ReDim Me.ElementMatrix(els, c)
            ReDim Me.TotalElements(els)

            Dim sum_e As Integer

            For i = 0 To els
                sum_e = 0
                For j = 0 To c
                    If Me.FlowSheet.SelectedCompounds(Me.ComponentIDs(j)).Elements.ContainsKey(Me.Elements(i)) Then
                        Me.ElementMatrix(i, j) = Me.FlowSheet.SelectedCompounds(Me.ComponentIDs(j)).Elements(Me.Elements(i))
                    Else
                        Me.ElementMatrix(i, j) = 0
                    End If
                    sum_e += N0(Me.ComponentIDs(j)) * Me.ElementMatrix(i, j)
                Next
                Me.TotalElements(i) = sum_e
            Next

        End Sub

#End Region

        Public Sub New(ByVal name As String, ByVal description As String)

            MyBase.New()
            Me.ComponentName = name
            Me.ComponentDescription = description

            Me._rex_iest = New ArrayList()
            Me._components = New List(Of String)
            ReDim ElementMatrix(0, 0)

        End Sub

        Public Overrides Sub Validate()

            If Not Me.GraphicObject.InputConnectors(0).IsAttached Then
                Throw New Exception(FlowSheet.GetTranslatedString("Nohcorrentedematriac10"))
            ElseIf Not Me.GraphicObject.OutputConnectors(0).IsAttached Then
                Throw New Exception(FlowSheet.GetTranslatedString("Nohcorrentedematriac11"))
            ElseIf Not Me.GraphicObject.OutputConnectors(1).IsAttached Then
                Throw New Exception(FlowSheet.GetTranslatedString("Nohcorrentedematriac11"))
            ElseIf Not Me.GraphicObject.OutputConnectors(0).IsAttached Then
                Throw New Exception(FlowSheet.GetTranslatedString("Verifiqueasconexesdo"))
            ElseIf Not Me.GraphicObject.OutputConnectors(1).IsAttached Then
                Throw New Exception(FlowSheet.GetTranslatedString("Verifiqueasconexesdo"))
            ElseIf Not Me.GraphicObject.InputConnectors(0).IsAttached Then
                Throw New Exception(FlowSheet.GetTranslatedString("Verifiqueasconexesdo"))
            End If

        End Sub

        Public Overrides Sub Calculate(Optional ByVal args As Object = Nothing)

            If Me.Conversions Is Nothing Then Me.m_conversions = New Dictionary(Of String, Double)
            If Me.ReactionExtents Is Nothing Then Me.m_reactionextents = New Dictionary(Of String, Double)
            If Me.ReactionExtentsEstimates Is Nothing Then Me._rex_iest = New ArrayList
            If Me.ComponentConversions Is Nothing Then Me.m_componentconversions = New Dictionary(Of String, Double)

            'first we validate the connections.

            Me.Validate()

            Dim i, j, l, m As Integer

            Me.Reactions.Clear()
            Me.ReactionExtents.Clear()
            Me.ReactionsSequence.Clear()
            Me.Conversions.Clear()
            Me.ComponentConversions.Clear()
            Me.DeltaQ = 0
            Me.DeltaT = 0

            Dim rx As Reaction
            ims = GetInletMaterialStream(0).Clone
            Dim pp As PropertyPackages.PropertyPackage = Me.PropertyPackage
            Dim ppr As New PropertyPackages.RaoultPropertyPackage()

            ims.SetFlowsheet(Me.FlowSheet)
            ims.PreferredFlashAlgorithmTag = Me.PreferredFlashAlgorithmTag

            'Reactants Enthalpy (kJ/kg * kg/s = kW) (ISOTHERMIC)
            Dim Hr0 As Double
            Hr0 = ims.Phases(0).Properties.enthalpy.GetValueOrDefault * ims.Phases(0).Properties.massflow.GetValueOrDefault

            Dim tmp As IFlashCalculationResult
            Dim xl, xv, H, S, wtotalx, wtotaly As Double
            pp.CurrentMaterialStream = ims

            'read temperature and pressure from inlet stream.

            T = ims.Phases(0).Properties.temperature.GetValueOrDefault
            P = ims.Phases(0).Properties.pressure.GetValueOrDefault
            P0 = 101325
            T0 = T

            'now check the selected solving method

            Select Case Me.SolvMethod

                Case SolvingMethod.DirectMinimization

                    Dim e, c As Integer
                    e = Me.Elements.Length - 1
                    c = Me.ComponentIDs.Count - 1
                    els = e
                    comps = c
                    tms = ims.Clone()
                    tms.SetFlowsheet(ims.FlowSheet)

                    Dim fm0(c), N0tot, W0tot, wm0 As Double

                    N0.Clear()
                    DN.Clear()
                    N.Clear()

                    'store initial values for molar flows

                    For Each cname As String In Me.ComponentIDs
                        N0.Add(cname, ims.Phases(0).Compounds(cname).MolarFlow.GetValueOrDefault)
                        DN.Add(cname, 0)
                        N.Add(cname, ims.Phases(0).Compounds(cname).MolarFlow.GetValueOrDefault)
                        wm0 += ims.Phases(0).Compounds(cname).MassFlow.GetValueOrDefault
                    Next

                    N0.Values.CopyTo(fm0, 0)

                    N0tot = ims.Phases(0).Properties.molarflow.GetValueOrDefault
                    W0tot = ims.Phases(0).Properties.massflow.GetValueOrDefault

                    Ninerts = N0tot - Sum(fm0)
                    Winerts = W0tot - wm0

                    'calculate the total amount of each element in the mixture and store values in a vector

                    Dim sum_e As Double
                    For i = 0 To els
                        sum_e = 0
                        For j = 0 To c
                            sum_e += N0(Me.ComponentIDs(j)) * Me.ElementMatrix(i, j)
                        Next
                        Me.TotalElements(i) = sum_e
                    Next

                    Me.ComponentConversions.Clear()
                    For Each s1 As String In Me.ComponentIDs
                        Me.ComponentConversions.Add(s1, 0)
                    Next

                    i = 0

                    'estimate initial values by solving linear problem using lp_solve

                    Calculator.CheckParallelPInvoke()

                    Dim lp As Integer
                    Dim release, Major, Minor, build As Integer

                    Dim re(c) As Double

                    lpsolve55.Init(".")
                    lp = lpsolve55.make_lp(e, c)
                    lpsolve55.version(Major, Minor, release, build)

                    'lpsolve55.print_str(lp, "lp_solve " & Major & "." & Minor & "." & release & "." & build & " demo" & vbLf & vbLf)

                    For i = 0 To e
                        For j = 0 To c
                            re(j) = Me.ElementMatrix(i, j)
                        Next
                        lpsolve55.add_constraint(lp, re, lpsolve55.lpsolve_constr_types.EQ, Me.TotalElements(i))
                    Next

                    'calculate ideal gas gibbs energy values

                    Dim igge(c) As Double

                    pp.CurrentMaterialStream = ims

                    For i = 0 To c
                        igge(i) = pp.AUX_DELGF_T(298.15, T, Me.ComponentIDs(i)) * FlowSheet.SelectedCompounds(Me.ComponentIDs(i)).Molar_Weight + Log(P / P0)
                        'igge(i) = igge(i) / (8.314 * T)
                        lpsolve55.set_lowbo(lp, i, 0)
                    Next

                    lpsolve55.set_obj_fn(lp, igge)
                    lpsolve55.set_minim(lp)
                    lpsolve55.solve(lp)

                    'the linear problem solution consists of only 'e' molar flows higher than zero.

                    Dim resc(c) As Double

                    lpsolve55.get_variables(lp, resc)

                    lpsolve55.delete_lp(lp)

                    'estimate lagrange multipliers

                    Dim lagrm(e) As Double

                    Dim mymat As New Mapack.Matrix(e + 1, e + 1)
                    Dim mypot As New Mapack.Matrix(e + 1, 1)
                    Dim mylags As New Mapack.Matrix(e + 1, 1)

                    Dim k As Integer = 0

                    For i = 0 To e
                        k = 0
                        For j = 0 To c
                            If resc(j) > 0 Then
                                mymat(i, k) = Me.ElementMatrix(i, j)
                                mypot(k, 0) = igge(j)
                                k += 1
                            End If
                        Next
                    Next

                    Try
                        mylags = mymat.Solve(mypot.Multiply(-1))
                        For i = 0 To e
                            lagrm(i) = mylags(i, 0)
                        Next
                    Catch ex As Exception
                        For i = 0 To e
                            lagrm(i) = igge(i) + 0.01
                        Next
                    End Try

                    'now setup optimization problem.
                    'the variables are molar flows and lagrange multipliers.
                    'define variable bounds.

                    Dim g0, g1, result(c + e + 1), ies(c + e + 1) As Double
                    Dim variables(c + e + 1) As OptBoundVariable

                    For i = 0 To c + e + 1
                        If i <= c Then
                            variables(i) = New OptBoundVariable("var" & CStr(i + 1), Me.InitialEstimates(i), 0.0000000001, 1.0E+20)
                            result(i) = N0(Me.ComponentIDs(i))
                            ies(i) = Me.InitialEstimates(i)
                            If ies(i) = 0 Then ies(i) = 0.00001 * N0tot
                        Else
                            variables(i) = New OptBoundVariable("var" & CStr(i + 1), lagrm(i - c - 1), -1.0E+20, 1.0E+20)
                            result(i) = variables(i).InitialGuess
                            ies(i) = variables(i).InitialGuess
                        End If
                    Next

                    'this call to FunctionValue2G returns the gibbs energy in kJ/s for the inlet stream - initial gibbs energy.

                    g0 = FunctionValue2G(result)

                    Me.InitialGibbsEnergy = g0

                    'solve using newton's method

                    Dim fx(c + e + 1), dfdx(c + e + 1, c + e + 1), dx(c + e + 1), x(c + e + 1), df, fval As Double
                    Dim brentsolver As New BrentOpt.BrentMinimize
                    brentsolver.DefineFuncDelegate(AddressOf MinimizeError)

                    Dim niter As Integer

                    x = ies
                    niter = 0
                    Do

                        tms = ims.Clone()
                        tms.SetFlowsheet(ims.FlowSheet)

                        fx = Me.FunctionValue2N(x)
                        dfdx = Me.FunctionGradient2N(x)

                        Dim success As Boolean
                        success = MathEx.SysLin.rsolve.rmatrixsolve(dfdx, fx, c + e + 2, dx)

                        tmpx = x
                        tmpdx = dx
                        df = 1.0#

                        'this call to the brent solver calculates the damping factor which minimizes the error (fval).

                        fval = 0.0# 'brentsolver.brentoptimize(0.01#, 2.0#, 0.0001, df)

                        Dim multipl As Double = 1.0#
                        'For i = 0 To c + e + 1
                        '    If i <= c And x(i) - dx(i) * df < 0 Then
                        '        If x(i) / (dx(i) * df) < multipl Then multipl = x(i) / (dx(i) * df)
                        '    End If
                        'Next

                        For i = 0 To c + e + 1
                            x(i) -= dx(i) * df * multipl
                            If x(i) <= 0 And i <= c Then x(i) = 0.000001 * N0tot
                        Next

                        niter += 1

                        If AbsSum(dx) = 0.0# Then
                            Throw New Exception("No solution found - reached a stationary point of the objective function (singular gradient matrix).")
                        End If

                        If Double.IsNaN(Sum(fx)) Then Throw New Exception("Calculation error")

                    Loop Until MathEx.Common.AbsSum(fx) < 0.001 Or niter > 249

                    If niter > 249 Then
                        Throw New Exception("Reached the maximum number of iterations without converging.")
                    End If

                    'reevaluate function

                    'this call to FunctionValue2G returns the final gibbs energy in kJ/s.

                    g1 = FunctionValue2G(x)

                    Me.FinalGibbsEnergy = g1

                    'this call to FunctionValue2FC returns the element material balance - should be very very close to zero.

                    _elbal = Me.FunctionValue2FC(x)

                    'calculate component conversions.

                    For Each sb As Compound In ims.Phases(0).Compounds.Values
                        If Me.ComponentConversions.ContainsKey(sb.Name) Then
                            Me.ComponentConversions(sb.Name) = -DN(sb.Name) / N0(sb.Name)
                        End If
                    Next

                    'reaction heat

                    Dim DHr As Double = 0

                    For Each sb As Compound In ims.Phases(0).Compounds.Values
                        If N0.ContainsKey(sb.Name) Then
                            DHr += sb.ConstantProperties.IG_Enthalpy_of_Formation_25C * sb.ConstantProperties.Molar_Weight * DN(sb.Name) / 1000
                        End If
                    Next

                    'Check to see if are negative molar fractions.

                    Dim sum1 As Double = 0
                    For Each subst As Compound In tms.Phases(0).Compounds.Values
                        If subst.MoleFraction.GetValueOrDefault < 0 Then
                            subst.MolarFlow = 0
                        Else
                            sum1 += subst.MolarFlow.GetValueOrDefault
                        End If
                    Next
                    For Each subst As Compound In tms.Phases(0).Compounds.Values
                        subst.MoleFraction = subst.MolarFlow.GetValueOrDefault / sum1
                    Next

                    ims = tms.Clone
                    ims.SetFlowsheet(tms.FlowSheet)

                    Me.PropertyPackage.CurrentMaterialStream = ims

                    Select Case Me.ReactorOperationMode

                        Case OperationMode.Adiabatic

                            Me.DeltaQ = 0.0#

                            'Products Enthalpy (kJ/kg * kg/s = kW)
                            Dim Hp = Hr0 - DHr

                            tmp = Me.PropertyPackage.CalculateEquilibrium2(FlashCalculationType.PressureEnthalpy, P, Hp / ims.Phases(0).Properties.massflow.GetValueOrDefault, 0)
                            Dim Tout As Double = tmp.CalculatedTemperature

                            Me.DeltaT = Tout - T
                            ims.Phases(0).Properties.temperature = Tout
                            T = ims.Phases(0).Properties.temperature.GetValueOrDefault

                            With pp
                                .CurrentMaterialStream = ims
                                .DW_CalcEquilibrium(PropertyPackages.FlashSpec.T, PropertyPackages.FlashSpec.P)
                                If ims.Phases(1).Properties.molarfraction.GetValueOrDefault > 0 Then
                                    .DW_CalcPhaseProps(PropertyPackages.Phase.Liquid)
                                Else
                                    .DW_ZerarPhaseProps(PropertyPackages.Phase.Liquid)
                                End If
                                If ims.Phases(2).Properties.molarfraction.GetValueOrDefault > 0 Then
                                    .DW_CalcPhaseProps(PropertyPackages.Phase.Vapor)
                                Else
                                    .DW_ZerarPhaseProps(PropertyPackages.Phase.Vapor)
                                End If
                                .DW_CalcPhaseProps(PropertyPackages.Phase.Mixture)
                                .DW_CalcOverallProps()
                                .DW_CalcTwoPhaseProps(PropertyPackages.Phase.Liquid, PropertyPackages.Phase.Vapor)
                                .DW_CalcCompMassFlow(-1)
                                .DW_CalcCompMolarFlow(-1)
                                .DW_CalcCompVolFlow(-1)
                                .DW_CalcVazaoVolumetrica()

                            End With

                        Case OperationMode.Isothermic

                            With pp
                                .CurrentMaterialStream = ims
                                'Calcular corrente de materia com T e P
                                '.DW_CalcVazaoMolar()
                                .DW_CalcEquilibrium(PropertyPackages.FlashSpec.T, PropertyPackages.FlashSpec.P)
                                If ims.Phases(1).Properties.molarfraction.GetValueOrDefault > 0 Then
                                    .DW_CalcPhaseProps(PropertyPackages.Phase.Liquid)
                                Else
                                    .DW_ZerarPhaseProps(PropertyPackages.Phase.Liquid)
                                End If
                                If ims.Phases(2).Properties.molarfraction.GetValueOrDefault > 0 Then
                                    .DW_CalcPhaseProps(PropertyPackages.Phase.Vapor)
                                Else
                                    .DW_ZerarPhaseProps(PropertyPackages.Phase.Vapor)
                                End If
                                .DW_CalcPhaseProps(PropertyPackages.Phase.Mixture)
                                .DW_CalcOverallProps()
                                .DW_CalcTwoPhaseProps(PropertyPackages.Phase.Liquid, PropertyPackages.Phase.Vapor)
                                .DW_CalcCompMassFlow(-1)
                                .DW_CalcCompMolarFlow(-1)
                                .DW_CalcCompVolFlow(-1)
                                .DW_CalcVazaoVolumetrica()

                            End With

                            'Products Enthalpy (kJ/kg * kg/s = kW)
                            Dim Hp = ims.Phases(0).Properties.enthalpy.GetValueOrDefault * ims.Phases(0).Properties.massflow.GetValueOrDefault

                            'Heat (kW)
                            Me.DeltaQ = Hp - Hr0 + DHr

                            Me.DeltaT = 0

                        Case OperationMode.OutletTemperature

                            Dim Tout As Double = Me.OutletTemperature

                            Me.DeltaT = Tout - T

                            ims.Phases(0).Properties.temperature = Tout

                            With pp
                                .CurrentMaterialStream = ims
                                'Calcular corrente de materia com T e P
                                '.DW_CalcVazaoMolar()
                                .DW_CalcEquilibrium(PropertyPackages.FlashSpec.T, PropertyPackages.FlashSpec.P)
                                If ims.Phases(1).Properties.molarfraction.GetValueOrDefault > 0 Then
                                    .DW_CalcPhaseProps(PropertyPackages.Phase.Liquid)
                                Else
                                    .DW_ZerarPhaseProps(PropertyPackages.Phase.Liquid)
                                End If
                                If ims.Phases(2).Properties.molarfraction.GetValueOrDefault > 0 Then
                                    .DW_CalcPhaseProps(PropertyPackages.Phase.Vapor)
                                Else
                                    .DW_ZerarPhaseProps(PropertyPackages.Phase.Vapor)
                                End If
                                .DW_CalcPhaseProps(PropertyPackages.Phase.Mixture)
                                .DW_CalcOverallProps()
                                .DW_CalcTwoPhaseProps(PropertyPackages.Phase.Liquid, PropertyPackages.Phase.Vapor)
                                .DW_CalcCompMassFlow(-1)
                                .DW_CalcCompMolarFlow(-1)
                                .DW_CalcCompVolFlow(-1)
                                .DW_CalcVazaoVolumetrica()

                            End With

                            'Products Enthalpy (kJ/kg * kg/s = kW)
                            Dim Hp = ims.Phases(0).Properties.enthalpy.GetValueOrDefault * ims.Phases(0).Properties.massflow.GetValueOrDefault

                            'Heat (kW)
                            Me.DeltaQ = Hp - Hr0 + DHr

                    End Select

                Case SolvingMethod.ReactionExtents

                    'check active reactions (equilibrium only) in the reaction set

                    For Each rxnsb As ReactionSetBase In FlowSheet.ReactionSets(Me.ReactionSetID).Reactions.Values
                        If FlowSheet.Reactions(rxnsb.ReactionID).ReactionType = ReactionType.Equilibrium And rxnsb.IsActive Then
                            Me.Reactions.Add(rxnsb.ReactionID)
                            Me.ReactionExtents.Add(rxnsb.ReactionID, 0)
                        End If
                    Next

                    'read stream conditions, which will be the same in the reactor.

                    'T0 = ims.Phases(0).Properties.temperature.GetValueOrDefault

                    Select Case Me.ReactorOperationMode
                        Case OperationMode.Adiabatic
                            T = T0 'initial value only, final value will be calculated by an iterative procedure
                        Case OperationMode.Isothermic
                            T = T0
                        Case OperationMode.OutletTemperature
                            T = OutletTemperature
                    End Select

                    ims.Phases(0).Properties.temperature = T

                    P = ims.Phases(0).Properties.pressure.GetValueOrDefault
                    P0 = 101325

                    pp.CurrentMaterialStream = ims
                    ppr.CurrentMaterialStream = ims


                    'initial estimates for reaction extents
                    tms = ims.Clone()

                    Me.ComponentConversions.Clear()
                    Me.ComponentIDs.Clear()

                    'r: number of reactions
                    'c: number of components
                    'i,j: iterators

                    i = 0
                    For Each rxid As String In Me.Reactions
                        rx = FlowSheet.Reactions(rxid)
                        j = 0
                        For Each comp As ReactionStoichBase In rx.Components.Values
                            If Not Me.ComponentIDs.Contains(comp.CompName) Then
                                Me.ComponentIDs.Add(comp.CompName)
                                Me.ComponentConversions.Add(comp.CompName, 0)
                            End If
                            j += 1
                        Next
                        i += 1
                    Next

                    r = Me.Reactions.Count - 1
                    c = Me.ComponentIDs.Count - 1
                    comps = c

                    ReDim E(c, r)


                    'E: matrix of stoichometric coefficients

                    i = 0
                    For Each rxid As String In Me.Reactions
                        rx = FlowSheet.Reactions(rxid)
                        j = 0
                        For Each cname As String In Me.ComponentIDs
                            If rx.Components.ContainsKey(cname) Then
                                E(j, i) = rx.Components(cname).StoichCoeff
                            Else
                                E(j, i) = 0
                            End If
                            j += 1
                        Next
                        i += 1
                    Next

                    Dim REx(r) As Double

                    For i = 0 To r
                        REx(i) = 0 'Me.ReactionExtentsEstimates(i)
                    Next

                    Dim fm0(c), N0tot, W0tot, wm0 As Double

                    N0.Clear()
                    DN.Clear()
                    N.Clear()

                    'store initial mole flows

                    For Each cname As String In Me.ComponentIDs
                        N0.Add(cname, ims.Phases(0).Compounds(cname).MolarFlow.GetValueOrDefault)
                        DN.Add(cname, 0)
                        N.Add(cname, ims.Phases(0).Compounds(cname).MolarFlow.GetValueOrDefault)
                        wm0 += ims.Phases(0).Compounds(cname).MassFlow.GetValueOrDefault
                    Next

                    N0.Values.CopyTo(fm0, 0)

                    N0tot = ims.Phases(0).Properties.molarflow.GetValueOrDefault
                    W0tot = ims.Phases(0).Properties.massflow.GetValueOrDefault

                    Ninerts = N0tot - Sum(fm0)
                    Winerts = W0tot - wm0

                    Dim lbound(Me.ReactionExtents.Count - 1) As Double
                    Dim ubound(Me.ReactionExtents.Count - 1) As Double
                    Dim var1 As Double

                    'define bounds for the extents.
                    i = 0
                    For Each rxid As String In Me.Reactions
                        rx = FlowSheet.Reactions(rxid)
                        l = 0
                        m = 0
                        For Each comp As ReactionStoichBase In rx.Components.Values
                            var1 = -N0(comp.CompName) / comp.StoichCoeff

                            If comp.StoichCoeff < 0 Then
                                If var1 < ubound(i) Or l = 0 Then ubound(i) = var1
                                l += 1
                            Else
                                If var1 > lbound(i) Or m = 0 Then lbound(i) = var1
                                m += 1
                            End If

                        Next
                        i += 1
                    Next

                    'solve the minimization problem using reaction extents as variables.
                    Dim variables(Me.ReactionExtents.Count - 1) As OptBoundVariable
                    For i = 0 To Me.ReactionExtents.Count - 1
                        variables(i) = New OptBoundVariable("ksi" & CStr(i + 1), 0, lbound(i), ubound(i))
                    Next


                    Dim g0, g1 As Double

                    'this call to FunctionValue returns the initial gibbs energy of the system.
                    g0 = FunctionValue(REx)
                    Me.InitialGibbsEnergy = g0


                    Dim CalcFinished As Boolean = False
                    Dim TLast As Double = T0 'remember T for iteration loops
                    Do
                        'use my own solver
                        cnt = 0

                        'use the Simplex solver to solve the minimization problem.
                        Dim solver As New Simplex
                        With solver
                            .Tolerance = 0.01
                            .MaxFunEvaluations = 1000
                            REx = .ComputeMin(AddressOf FunctionValue, variables)
                        End With

                        'reevaluate function
                        'this call to FunctionValue returns the final gibbs energy of the system.
                        g1 = FunctionValue(REx)
                        Me.FinalGibbsEnergy = g1

                        i = 0
                        For Each r As String In Me.Reactions
                            Me.ReactionExtents(r) = REx(i)
                            i += 1
                        Next

                        Dim DHr, Hp As Double

                        DHr = 0

                        i = 0
                        Do
                            'process reaction i
                            rx = FlowSheet.Reactions(Me.Reactions(i))

                            Dim id(rx.Components.Count - 1) As String
                            Dim stcoef(rx.Components.Count - 1) As Double
                            Dim bcidx As Integer = 0
                            j = 0
                            For Each sb As ReactionStoichBase In rx.Components.Values
                                id(j) = sb.CompName
                                stcoef(j) = sb.StoichCoeff
                                If sb.IsBaseReactant Then bcidx = j
                                j += 1
                            Next

                            'Heat released (or absorbed) (kJ/s = kW) (Ideal Gas)
                            DHr += rx.ReactionHeat * Me.ReactionExtents(Me.Reactions(i)) * rx.Components(rx.BaseReactant).StoichCoeff / 1000
                            i += 1
                        Loop Until i = Me.Reactions.Count


                        ' comp. conversions
                        For Each sb As Compound In ims.Phases(0).Compounds.Values
                            If Me.ComponentConversions.ContainsKey(sb.Name) Then
                                Me.ComponentConversions(sb.Name) = -DN(sb.Name) / N0(sb.Name)
                            End If
                        Next

                        'Check to see if there are negative molar fractions.
                        Dim sum1 As Double = 0
                        For Each subst As Compound In tms.Phases(0).Compounds.Values
                            If subst.MoleFraction.GetValueOrDefault < 0 Then
                                subst.MolarFlow = 0
                            Else
                                sum1 += subst.MolarFlow.GetValueOrDefault
                            End If
                        Next
                        For Each subst As Compound In tms.Phases(0).Compounds.Values
                            subst.MoleFraction = subst.MolarFlow.GetValueOrDefault / sum1
                        Next

                        ims = tms.Clone
                        ims.SetFlowsheet(tms.FlowSheet)

                        Select Case Me.ReactorOperationMode

                            Case OperationMode.Adiabatic

                                'Me.DeltaQ = FlowSheet.SimulationObjects(Me.GraphicObject.InputConnectors(1).AttachedConnector.AttachedFrom.Name).EnergyFlow.GetValueOrDefault
                                Me.DeltaQ = 0.0# 'adiabatic !

                                'Products Enthalpy (kJ/kg * kg/s = kW)
                                Hp = Hr0 + DHr

                                tmp = Me.PropertyPackage.CalculateEquilibrium2(FlashCalculationType.PressureEnthalpy, P, Hp / ims.Phases(0).Properties.massflow.GetValueOrDefault, 0)
                                T = tmp.CalculatedTemperature

                                If Math.Abs(T - TLast) < 0.1 Then CalcFinished = True
                                TLast = T
                                Me.DeltaT = T - T0

                                ims.Phases(0).Properties.temperature = T

                                With pp
                                    .CurrentMaterialStream = ims
                                    'Calcular corrente de materia com T e P
                                    '.DW_CalcVazaoMolar()
                                    .DW_CalcEquilibrium(PropertyPackages.FlashSpec.T, PropertyPackages.FlashSpec.P)
                                    If ims.Phases(1).Properties.molarfraction.GetValueOrDefault > 0 Then
                                        .DW_CalcPhaseProps(PropertyPackages.Phase.Liquid)
                                    Else
                                        .DW_ZerarPhaseProps(PropertyPackages.Phase.Liquid)
                                    End If
                                    If ims.Phases(2).Properties.molarfraction.GetValueOrDefault > 0 Then
                                        .DW_CalcPhaseProps(PropertyPackages.Phase.Vapor)
                                    Else
                                        .DW_ZerarPhaseProps(PropertyPackages.Phase.Vapor)
                                    End If
                                    .DW_CalcPhaseProps(PropertyPackages.Phase.Mixture)
                                    .DW_CalcOverallProps()
                                    .DW_CalcTwoPhaseProps(PropertyPackages.Phase.Liquid, PropertyPackages.Phase.Vapor)
                                    .DW_CalcCompMassFlow(-1)
                                    .DW_CalcCompMolarFlow(-1)
                                    .DW_CalcCompVolFlow(-1)
                                    .DW_CalcVazaoVolumetrica()

                                End With

                            Case OperationMode.Isothermic

                                With pp
                                    .CurrentMaterialStream = ims
                                    'Calcular corrente de materia com T e P
                                    '.DW_CalcVazaoMolar()
                                    .DW_CalcEquilibrium(PropertyPackages.FlashSpec.T, PropertyPackages.FlashSpec.P)
                                    If ims.Phases(1).Properties.molarfraction.GetValueOrDefault > 0 Then
                                        .DW_CalcPhaseProps(PropertyPackages.Phase.Liquid)
                                    Else
                                        .DW_ZerarPhaseProps(PropertyPackages.Phase.Liquid)
                                    End If
                                    If ims.Phases(2).Properties.molarfraction.GetValueOrDefault > 0 Then

                                        .DW_CalcPhaseProps(PropertyPackages.Phase.Vapor)
                                    Else
                                        .DW_ZerarPhaseProps(PropertyPackages.Phase.Vapor)
                                    End If
                                    .DW_CalcPhaseProps(PropertyPackages.Phase.Mixture)
                                    .DW_CalcOverallProps()
                                    .DW_CalcTwoPhaseProps(PropertyPackages.Phase.Liquid, PropertyPackages.Phase.Vapor)
                                    .DW_CalcCompMassFlow(-1)
                                    .DW_CalcCompMolarFlow(-1)
                                    .DW_CalcCompVolFlow(-1)
                                    .DW_CalcVazaoVolumetrica()

                                End With

                                'Products Enthalpy (kJ/kg * kg/s = kW)
                                Hp = ims.Phases(0).Properties.enthalpy.GetValueOrDefault * ims.Phases(0).Properties.massflow.GetValueOrDefault

                                'Heat (kW)
                                Me.DeltaQ = Hp - Hr0 - DHr

                                Me.DeltaT = 0
                                CalcFinished = True

                            Case OperationMode.OutletTemperature

                                'Dim Tout As Double = Me.OutletTemperature

                                Me.DeltaT = T - T0

                                ims.Phases(0).Properties.temperature = OutletTemperature
                                With pp
                                    .CurrentMaterialStream = ims
                                    'Calcular corrente de materia com T e P
                                    '.DW_CalcVazaoMolar()
                                    .DW_CalcEquilibrium(PropertyPackages.FlashSpec.T, PropertyPackages.FlashSpec.P)
                                    If ims.Phases(1).Properties.molarfraction.GetValueOrDefault > 0 Then
                                        .DW_CalcPhaseProps(PropertyPackages.Phase.Liquid)
                                    Else
                                        .DW_ZerarPhaseProps(PropertyPackages.Phase.Liquid)
                                    End If
                                    If ims.Phases(2).Properties.molarfraction.GetValueOrDefault > 0 Then
                                        .DW_CalcPhaseProps(PropertyPackages.Phase.Vapor)
                                    Else
                                        .DW_ZerarPhaseProps(PropertyPackages.Phase.Vapor)
                                    End If
                                    .DW_CalcPhaseProps(PropertyPackages.Phase.Mixture)
                                    .DW_CalcOverallProps()
                                    .DW_CalcTwoPhaseProps(PropertyPackages.Phase.Liquid, PropertyPackages.Phase.Vapor)
                                    .DW_CalcCompMassFlow(-1)
                                    .DW_CalcCompMolarFlow(-1)
                                    .DW_CalcCompVolFlow(-1)
                                    .DW_CalcVazaoVolumetrica()

                                End With

                                'Products Enthalpy (kJ/kg * kg/s = kW)
                                Hp = ims.Phases(0).Properties.enthalpy.GetValueOrDefault * ims.Phases(0).Properties.massflow.GetValueOrDefault

                                'Heat (kW)
                                Me.DeltaQ = Hp - Hr0 - DHr

                                CalcFinished = True
                        End Select

                    Loop Until CalcFinished

            End Select


            Dim W As Double = ims.Phases(0).Properties.massflow.GetValueOrDefault

            pp.CurrentMaterialStream = ims

            'do a flash calc (calculate final temperature/enthalpy)
            tmp = pp.CalculateEquilibrium2(FlashCalculationType.PressureTemperature, ims.Phases(0).Properties.pressure.GetValueOrDefault, ims.Phases(0).Properties.temperature.GetValueOrDefault, 0)

            'Return New Object() {xl, xv, T, P, H, S, 1, 1, Vx, Vy}
            Dim Vx(ims.Phases(0).Compounds.Count - 1), Vy(ims.Phases(0).Compounds.Count - 1), Vwx(ims.Phases(0).Compounds.Count - 1), Vwy(ims.Phases(0).Compounds.Count - 1) As Double
            xl = tmp.GetLiquidPhase1MoleFraction
            xv = tmp.GetVaporPhaseMoleFraction
            'T = tmp.CalculatedTemperature
            'P = tmp.CalculatedPressure
            H = tmp.CalculatedEnthalpy
            S = tmp.CalculatedEntropy
            Vx = tmp.GetLiquidPhase1MoleFractions
            Vy = tmp.GetVaporPhaseMoleFractions

            Dim ms As MaterialStream
            Dim cp As ConnectionPoint
            cp = Me.GraphicObject.InputConnectors(0)
            If cp.IsAttached Then
                ms = FlowSheet.SimulationObjects(cp.AttachedConnector.AttachedFrom.Name)
                Dim comp As BaseClasses.Compound
                i = 0
                For Each comp In ms.Phases(0).Compounds.Values
                    wtotalx += Vx(i) * comp.ConstantProperties.Molar_Weight
                    wtotaly += Vy(i) * comp.ConstantProperties.Molar_Weight
                    i += 1
                Next
                i = 0
                For Each comp In ms.Phases(0).Compounds.Values
                    Vwx(i) = Vx(i) * comp.ConstantProperties.Molar_Weight / wtotalx
                    Vwy(i) = Vy(i) * comp.ConstantProperties.Molar_Weight / wtotaly
                    i += 1
                Next
            End If

            cp = Me.GraphicObject.OutputConnectors(0)
            If cp.IsAttached Then
                ms = FlowSheet.SimulationObjects(cp.AttachedConnector.AttachedTo.Name)
                With ms
                    .ClearAllProps()
                    .Phases(0).Properties.temperature = T
                    .Phases(0).Properties.pressure = P
                    .Phases(0).Properties.enthalpy = H * (wtotaly * xv / (wtotaly * xv + wtotalx * xl))
                    Dim comp As BaseClasses.Compound
                    j = 0
                    For Each comp In .Phases(0).Compounds.Values
                        comp.MoleFraction = Vy(j)
                        comp.MassFraction = Vwy(j)
                        j += 1
                    Next
                    .Phases(0).Properties.massflow = W * (wtotaly * xv / (wtotaly * xv + wtotalx * xl))
                End With
            End If

            cp = Me.GraphicObject.OutputConnectors(1)
            If cp.IsAttached Then
                ms = FlowSheet.SimulationObjects(cp.AttachedConnector.AttachedTo.Name)
                With ms
                    .ClearAllProps()
                    .Phases(0).Properties.temperature = T
                    .Phases(0).Properties.pressure = P
                    .Phases(0).Properties.enthalpy = H * (wtotalx * xl / (wtotaly * xv + wtotalx * xl))
                    Dim comp As BaseClasses.Compound
                    j = 0
                    For Each comp In .Phases(0).Compounds.Values
                        comp.MoleFraction = Vx(j)
                        comp.MassFraction = Vwx(j)
                        j += 1
                    Next
                    .Phases(0).Properties.massflow = W * (wtotalx * xl / (wtotaly * xv + wtotalx * xl))
                End With
            End If

            'Corrente de EnergyFlow - atualizar valor da potencia (kJ/s)
            With GetInletEnergyStream(1)
                .EnergyFlow = Me.DeltaQ.GetValueOrDefault
                .GraphicObject.Calculated = True
            End With

        End Sub

        Public Overrides Sub DeCalculate()

            Dim j As Integer

            Dim ms As MaterialStream
            Dim cp As ConnectionPoint

            cp = Me.GraphicObject.OutputConnectors(0)
            If cp.IsAttached Then
                ms = FlowSheet.SimulationObjects(cp.AttachedConnector.AttachedTo.Name)
                With ms
                    .Phases(0).Properties.temperature = Nothing
                    .Phases(0).Properties.pressure = Nothing
                    .Phases(0).Properties.enthalpy = Nothing
                    Dim comp As BaseClasses.Compound
                    j = 0
                    For Each comp In .Phases(0).Compounds.Values
                        comp.MoleFraction = 0
                        comp.MassFraction = 0
                        j += 1
                    Next
                    .Phases(0).Properties.massflow = Nothing
                    .Phases(0).Properties.massfraction = 1
                    .Phases(0).Properties.molarfraction = 1
                    .GraphicObject.Calculated = False
                End With
            End If

            cp = Me.GraphicObject.OutputConnectors(1)
            If cp.IsAttached Then
                ms = FlowSheet.SimulationObjects(cp.AttachedConnector.AttachedTo.Name)
                With ms
                    .Phases(0).Properties.temperature = Nothing
                    .Phases(0).Properties.pressure = Nothing
                    .Phases(0).Properties.enthalpy = Nothing
                    Dim comp As BaseClasses.Compound
                    j = 0
                    For Each comp In .Phases(0).Compounds.Values
                        comp.MoleFraction = 0
                        comp.MassFraction = 0
                        j += 1
                    Next
                    .Phases(0).Properties.massflow = Nothing
                    .Phases(0).Properties.massfraction = 1
                    .Phases(0).Properties.molarfraction = 1
                    .GraphicObject.Calculated = False
                End With
            End If

        End Sub

        Public Overrides Function GetPropertyValue(ByVal prop As String, Optional ByVal su As Interfaces.IUnitsOfMeasure = Nothing) As Object
            If su Is Nothing Then su = New SystemsOfUnits.SI
            Dim cv As New SystemsOfUnits.Converter
            Dim value As Double = 0
            Dim propidx As Integer = Convert.ToInt32(prop.Split("_")(2))

            Select Case propidx
                Case 0
                    'PROP_GR_0	Pressure Drop
                    value = SystemsOfUnits.Converter.ConvertFromSI(su.deltaP, Me.DeltaP.GetValueOrDefault)
                Case 0
                    'PROP_GR_1	Outlet Temperature
                    value = SystemsOfUnits.Converter.ConvertFromSI(su.temperature, Me.OutletTemperature)
            End Select

            Return value
        End Function

        Public Overloads Overrides Function GetProperties(ByVal proptype As Interfaces.Enums.PropertyType) As String()
            Dim i As Integer = 0
            Dim proplist As New ArrayList
            Select Case proptype
                Case PropertyType.RW
                    For i = 0 To 1
                        proplist.Add("PROP_GR_" + CStr(i))
                    Next
                Case PropertyType.WR
                    For i = 0 To 1
                        proplist.Add("PROP_GR_" + CStr(i))
                    Next
                Case PropertyType.ALL
                    For i = 0 To 1
                        proplist.Add("PROP_GR_" + CStr(i))
                    Next
            End Select
            Return proplist.ToArray(GetType(System.String))
            proplist = Nothing
        End Function

        Public Overrides Function SetPropertyValue(ByVal prop As String, ByVal propval As Object, Optional ByVal su As Interfaces.IUnitsOfMeasure = Nothing) As Boolean
            If su Is Nothing Then su = New SystemsOfUnits.SI
            Dim cv As New SystemsOfUnits.Converter
            Dim propidx As Integer = Convert.ToInt32(prop.Split("_")(2))

            Select Case propidx

                Case 0
                    'PROP_GR_0	Pressure Drop
                    Me.DeltaP = SystemsOfUnits.Converter.ConvertToSI(su.deltaP, propval)
                Case 1
                    'PROP_GR_1	Outlet Temperature
                    Me.OutletTemperature = SystemsOfUnits.Converter.ConvertToSI(su.temperature, propval)

            End Select
            Return 1
        End Function

        Public Overrides Function GetPropertyUnit(ByVal prop As String, Optional ByVal su As Interfaces.IUnitsOfMeasure = Nothing) As String
            If su Is Nothing Then su = New SystemsOfUnits.SI
            Dim cv As New SystemsOfUnits.Converter
            Dim value As String = ""
            Dim propidx As Integer = Convert.ToInt32(prop.Split("_")(2))

            Select Case propidx

                Case 0
                    'PROP_GR_0	Pressure Drop
                    value = su.deltaP
                Case 1
                    'PROP_GR_1	Outlet Temperature
                    value = su.temperature
            End Select

            Return value
        End Function

        Public Overrides Sub DisplayEditForm()

            If f Is Nothing Then
                f = New EditingForm_ReactorConvEqGibbs With {.SimObject = Me}
                f.ShowHint = GlobalSettings.Settings.DefaultEditFormLocation
                Me.FlowSheet.DisplayForm(f)
            Else
                If f.IsDisposed Then
                    f = New EditingForm_ReactorConvEqGibbs With {.SimObject = Me}
                    f.ShowHint = GlobalSettings.Settings.DefaultEditFormLocation
                    Me.FlowSheet.DisplayForm(f)
                Else
                    f.Activate()
                End If
            End If

        End Sub

        Public Overrides Sub UpdateEditForm()
            If f IsNot Nothing Then
                If Not f.IsDisposed Then
                    f.UIThread(Sub() f.UpdateInfo())
                End If
            End If
        End Sub

        Public Overrides Function GetIconBitmap() As Object
            Return My.Resources.re_gibbs_32
        End Function

        Public Overrides Function GetDisplayDescription() As String
            If GlobalSettings.Settings.CurrentCulture = "pt-BR" Then
                Return "Calcula o equil�brio qu�mico de �tomos em uma Corrente de Mat�ria"
            Else
                Return "Calculates chemical/atomic equilibrium for a Material Stream"
            End If
        End Function

        Public Overrides Function GetDisplayName() As String
            If GlobalSettings.Settings.CurrentCulture = "pt-BR" Then
                Return "Reator de Gibbs"
            Else
                Return "Gibbs Reactor"
            End If
        End Function

        Public Overrides Sub CloseEditForm()
            If f IsNot Nothing Then
                If Not f.IsDisposed Then
                    f.Close()
                    f = Nothing
                End If
            End If
        End Sub

    End Class

End Namespace

