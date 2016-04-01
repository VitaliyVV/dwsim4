﻿'    Recycle Calculation Routines 
'    Copyright 2008 Daniel Wagner O. de Medeiros
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
Imports DWSIM.DWSIM.SimulationObjects.SpecialOps.Helpers.Recycle
Imports System.Linq
Imports System.ComponentModel
Imports PropertyGridEx
Imports DWSIM.Thermodynamics

Namespace DWSIM.SimulationObjects.SpecialOps

    <System.Serializable()> Public Class Recycle

        Inherits DWSIM.SimulationObjects.UnitOperations.UnitOpBaseClass

        Protected m_ConvPar As ConvergenceParameters
        Protected m_ConvHist As ConvergenceHistory
        Protected m_AccelMethod As AccelMethod = AccelMethod.GlobalBroyden
        Protected m_WegPars As WegsteinParameters
        Protected m_FlashType As FlashType = Helpers.Recycle.FlashType.None

        Protected m_MaxIterations As Integer = 50
        Protected m_IterationCount As Integer = 0
        Protected m_InternalCounterT As Integer = 0
        Protected m_InternalCounterP As Integer = 0
        Protected m_InternalCounterW As Integer = 0
        Protected m_IterationsTaken As Integer = 0

        Public Property Converged As Boolean = False

        Public Property CopyOnStreamDataError As Boolean = False

        Protected m_Errors As New Dictionary(Of String, Double)
        Protected m_Values As New Dictionary(Of String, Double)

        Public ReadOnly Property Errors As Dictionary(Of String, Double)
            Get
                Return m_Errors
            End Get
        End Property

        Public ReadOnly Property Values As Dictionary(Of String, Double)
            Get
                Return m_Values
            End Get
        End Property

        Public Overrides Function LoadData(data As System.Collections.Generic.List(Of System.Xml.Linq.XElement)) As Boolean

            Dim ci As Globalization.CultureInfo = Globalization.CultureInfo.InvariantCulture

            MyBase.LoadData(data)

            Dim xel As XElement

            xel = (From xel2 As XElement In data Select xel2 Where xel2.Name = "WegPars").SingleOrDefault

            If Not xel Is Nothing Then
                m_WegPars.AccelDelay = Double.Parse(xel.@AccelDelay, ci)
                m_WegPars.AccelFreq = Double.Parse(xel.@AccelFreq, ci)
                m_WegPars.Qmax = Double.Parse(xel.@Qmax, ci)
                m_WegPars.Qmin = Double.Parse(xel.@Qmin, ci)
            End If

        End Function

        Public Overrides Function SaveData() As System.Collections.Generic.List(Of System.Xml.Linq.XElement)

            Dim elements As System.Collections.Generic.List(Of System.Xml.Linq.XElement) = MyBase.SaveData()
            Dim ci As Globalization.CultureInfo = Globalization.CultureInfo.InvariantCulture

            With elements
                .Add(New XElement("WegPars", New XAttribute("AccelDelay", m_WegPars.AccelDelay),
                                  New XAttribute("AccelFreq", m_WegPars.AccelFreq),
                                  New XAttribute("Qmax", m_WegPars.Qmax),
                                  New XAttribute("Qmin", m_WegPars.Qmin)))
            End With

            Return elements

        End Function

        Public Property IterationsTaken() As Integer
            Get
                Return m_IterationsTaken
            End Get
            Set(ByVal value As Integer)
                m_IterationsTaken = value
            End Set
        End Property

        Public Property IterationCount() As Integer
            Get
                Return m_IterationCount
            End Get
            Set(ByVal value As Integer)
                m_IterationCount = value
            End Set
        End Property

        Public Property FlashType() As FlashType
            Get
                Return m_FlashType
            End Get
            Set(ByVal value As FlashType)
                m_FlashType = value
            End Set
        End Property

        Public Property WegsteinParameters() As WegsteinParameters
            Get
                Return m_WegPars
            End Get
            Set(ByVal value As WegsteinParameters)
                m_WegPars = value
            End Set
        End Property

        Public Property AccelerationMethod() As AccelMethod
            Get
                Return m_AccelMethod
            End Get
            Set(ByVal value As AccelMethod)
                m_AccelMethod = value
            End Set
        End Property

        Public Property ConvergenceParameters() As ConvergenceParameters
            Get
                Return m_ConvPar
            End Get
            Set(ByVal value As ConvergenceParameters)
                m_ConvPar = value
            End Set
        End Property

        Public Property ConvergenceHistory() As ConvergenceHistory
            Get
                Return m_ConvHist
            End Get
            Set(ByVal value As ConvergenceHistory)
                m_ConvHist = value
            End Set
        End Property

        Public Property MaximumIterations() As Integer
            Get
                Return Me.m_MaxIterations
            End Get
            Set(ByVal value As Integer)
                Me.m_MaxIterations = value
            End Set
        End Property

        Public Sub New()

            MyBase.CreateNew()

            m_ConvPar = New ConvergenceParameters
            m_ConvHist = New ConvergenceHistory
            m_WegPars = New WegsteinParameters

        End Sub

        Public Sub New(ByVal name As String, ByVal description As String)

            MyBase.CreateNew()

            m_ConvPar = New ConvergenceParameters
            m_ConvHist = New ConvergenceHistory
            m_WegPars = New WegsteinParameters

            Me.ComponentName = name
            Me.ComponentDescription = description



        End Sub

        Public Sub SetOutletStreamProperties()

            Dim msfrom, msto As DWSIM.SimulationObjects.Streams.MaterialStream

            If Me.GraphicObject.InputConnectors(0).IsAttached Then
                msfrom = FlowSheet.SimulationObjects(Me.GraphicObject.InputConnectors(0).AttachedConnector.AttachedFrom.Name)
            Else
                msfrom = Nothing
            End If

            If Me.GraphicObject.OutputConnectors(0).IsAttached Then
                msto = FlowSheet.SimulationObjects(Me.GraphicObject.OutputConnectors(0).AttachedConnector.AttachedTo.Name)
                With msto

                    .PropertyPackage.CurrentMaterialStream = msto
                    .Phases(0).Properties.temperature = Values("Temperature")
                    .Phases(0).Properties.pressure = Values("Pressure")
                    .Phases(0).Properties.massflow = Values("MassFlow")
                    .Phases(0).Properties.enthalpy = Values("Enthalpy")

                    For Each comp In .Phases(0).Compounds.Values
                        comp.MoleFraction = msfrom.Phases(0).Compounds(comp.Name).MoleFraction
                    Next

                    .CalcOverallCompMassFractions()

                End With
            End If


        End Sub

        Public Overrides Function Calculate(Optional ByVal args As Object = Nothing) As Integer

            Dim form As Global.DWSIM.IFLowsheet = Me.FlowSheet
            Dim objargs As New DWSIM.Extras.StatusChangeEventArgs

            If Not Me.GraphicObject.OutputConnectors(0).IsAttached Then
                'Call function to calculate flowsheet
                With objargs
                    .Calculated = False
                    .Name = Me.Name
                    .ObjectType = ObjectType.OT_Recycle
                End With
                Throw New Exception(DWSIM.App.GetLocalString("Nohcorrentedematriac7"))
            ElseIf Not Me.GraphicObject.InputConnectors(0).IsAttached Then
                With objargs
                    .Calculated = False
                    .Name = Me.Name
                    .ObjectType = ObjectType.OT_Recycle
                End With
                Throw New Exception(DWSIM.App.GetLocalString("Verifiqueasconexesdo"))
            End If

            Dim Tnew, Pnew, Wnew, Hnew, Snew As Double

            Dim ems As DWSIM.SimulationObjects.Streams.MaterialStream = form.Collections.FlowsheetObjectCollection(Me.GraphicObject.InputConnectors(0).AttachedConnector.AttachedFrom.Name)

            With ems.Phases(0).Properties

                Me.ConvergenceHistory.TemperaturaE = .temperature.GetValueOrDefault - Me.ConvergenceHistory.Temperatura
                Me.ConvergenceHistory.PressaoE = .pressure.GetValueOrDefault - Me.ConvergenceHistory.Pressao
                Me.ConvergenceHistory.VazaoMassicaE = .massflow.GetValueOrDefault - Me.ConvergenceHistory.VazaoMassica

                Me.ConvergenceHistory.TemperaturaE0 = Me.ConvergenceHistory.Temperatura - Me.ConvergenceHistory.Temperatura0
                Me.ConvergenceHistory.PressaoE0 = Me.ConvergenceHistory.Pressao - Me.ConvergenceHistory.Pressao0
                Me.ConvergenceHistory.VazaoMassicaE0 = Me.ConvergenceHistory.VazaoMassica - Me.ConvergenceHistory.VazaoMassica0

                Me.ConvergenceHistory.Temperatura0 = Me.ConvergenceHistory.Temperatura
                Me.ConvergenceHistory.Pressao0 = Me.ConvergenceHistory.Pressao
                Me.ConvergenceHistory.VazaoMassica0 = Me.ConvergenceHistory.VazaoMassica

                Me.ConvergenceHistory.Temperatura = .temperature.GetValueOrDefault
                Me.ConvergenceHistory.Pressao = .pressure.GetValueOrDefault
                Me.ConvergenceHistory.VazaoMassica = .massflow.GetValueOrDefault

                Hnew = .enthalpy.GetValueOrDefault
                Snew = .entropy.GetValueOrDefault

                If Me.Errors.Count = 0 Then
                    Me.Errors.Add("Temperature", .temperature.GetValueOrDefault)
                    Me.Errors.Add("Pressure", .pressure.GetValueOrDefault)
                    Me.Errors.Add("MassFlow", .massflow.GetValueOrDefault)
                    Me.Errors.Add("Enthalpy", .enthalpy.GetValueOrDefault)
                Else
                    Me.Errors("Temperature") = Me.Values("Temperature") - .temperature.GetValueOrDefault
                    Me.Errors("Pressure") = Me.Values("Pressure") - .pressure.GetValueOrDefault
                    Me.Errors("MassFlow") = Me.Values("MassFlow") - .massflow.GetValueOrDefault
                    Me.Errors("Enthalpy") = Me.Values("Enthalpy") - .enthalpy.GetValueOrDefault
                End If

            End With

            Dim oms As DWSIM.SimulationObjects.Streams.MaterialStream = form.Collections.FlowsheetObjectCollection(Me.GraphicObject.OutputConnectors(0).AttachedConnector.AttachedTo.Name)

            With oms.Phases(0).Properties

                If Me.Values.Count = 0 Then
                    Me.Values.Add("Temperature", .temperature.GetValueOrDefault)
                    Me.Values.Add("Pressure", .pressure.GetValueOrDefault)
                    Me.Values.Add("MassFlow", .massflow.GetValueOrDefault)
                    Me.Values.Add("Enthalpy", .enthalpy.GetValueOrDefault)
                Else
                    Me.Values("Temperature") = .temperature.GetValueOrDefault
                    Me.Values("Pressure") = .pressure.GetValueOrDefault
                    Me.Values("MassFlow") = .massflow.GetValueOrDefault
                    Me.Values("Enthalpy") = .enthalpy.GetValueOrDefault
                End If

            End With

            If Me.IterationCount <= 3 Then

                Tnew = Me.ConvergenceHistory.Temperatura
                Pnew = Me.ConvergenceHistory.Pressao
                Wnew = Me.ConvergenceHistory.VazaoMassica

            Else

                Select Case Me.AccelerationMethod

                    Case AccelMethod.None

                        Tnew = Me.ConvergenceHistory.Temperatura
                        Pnew = Me.ConvergenceHistory.Pressao
                        Wnew = Me.ConvergenceHistory.VazaoMassica

                    Case AccelMethod.Wegstein

                        If Me.WegsteinParameters.AccelDelay <= Me.IterationCount + 3 Then

                            Dim sT, sP, sW As Double
                            Dim qT, qP, qW As Double
                            sT = (Me.ConvergenceHistory.TemperaturaE - Me.ConvergenceHistory.TemperaturaE0) / (Me.ConvergenceHistory.Temperatura - Me.ConvergenceHistory.Temperatura0)
                            sP = (Me.ConvergenceHistory.PressaoE - Me.ConvergenceHistory.PressaoE0) / (Me.ConvergenceHistory.Pressao - Me.ConvergenceHistory.Pressao0)
                            sW = (Me.ConvergenceHistory.VazaoMassicaE - Me.ConvergenceHistory.VazaoMassicaE0) / (Me.ConvergenceHistory.VazaoMassica - Me.ConvergenceHistory.VazaoMassica0)
                            qT = sT / (sT - 1)
                            qP = sP / (sP - 1)
                            qW = sW / (sW - 1)
                            If Me.WegsteinParameters.AccelFreq <= Me.m_InternalCounterT And Double.IsNaN(sT) = False And qT > Me.WegsteinParameters.Qmin And qT < Me.WegsteinParameters.Qmax Then
                                Tnew = Me.ConvergenceHistory.TemperaturaE * (1 - qT) + Me.ConvergenceHistory.Temperatura * qT
                                Me.m_InternalCounterT = 0
                            Else
                                Tnew = Me.ConvergenceHistory.Temperatura
                                Me.m_InternalCounterT += 1
                            End If
                            If Me.WegsteinParameters.AccelFreq <= Me.m_InternalCounterP And Double.IsNaN(sP) = False And qP > Me.WegsteinParameters.Qmin And qP < Me.WegsteinParameters.Qmax Then
                                Pnew = Me.ConvergenceHistory.PressaoE * (1 - qP) + Me.ConvergenceHistory.Pressao * qP
                                Me.m_InternalCounterP = 0
                            Else
                                Pnew = Me.ConvergenceHistory.Pressao
                                Me.m_InternalCounterP += 1
                            End If
                            If Me.WegsteinParameters.AccelFreq <= Me.m_InternalCounterW And Double.IsNaN(sW) = False And qW > Me.WegsteinParameters.Qmin And qW < Me.WegsteinParameters.Qmax Then
                                Wnew = Me.ConvergenceHistory.VazaoMassicaE * (1 - qW) + Me.ConvergenceHistory.VazaoMassica * qW
                                Me.m_InternalCounterW = 0
                            Else
                                Wnew = Me.ConvergenceHistory.VazaoMassica
                                Me.m_InternalCounterW += 1
                            End If

                        Else

                            Tnew = Me.ConvergenceHistory.Temperatura
                            Pnew = Me.ConvergenceHistory.Pressao
                            Wnew = Me.ConvergenceHistory.VazaoMassica

                        End If

                    Case AccelMethod.Dominant_Eigenvalue

                        Dim eT(1), eP(1), eW(1), M As Double

                        eT(0) = Me.ConvergenceHistory.TemperaturaE0
                        eT(1) = Me.ConvergenceHistory.TemperaturaE
                        eP(0) = Me.ConvergenceHistory.PressaoE0
                        eP(1) = Me.ConvergenceHistory.PressaoE
                        eW(0) = Me.ConvergenceHistory.VazaoMassicaE0
                        eW(1) = Me.ConvergenceHistory.VazaoMassicaE

                        Dim ve0 As Double() = New Double() {Math.Abs(eT(0)), Math.Abs(eP(0)), Math.Abs(eW(0))}
                        Dim ve1 As Double() = New Double() {Math.Abs(eT(1)), Math.Abs(eP(1)), Math.Abs(eW(1))}

                        M = MAX(ve1) / MAX(ve0)

                        With Me.ConvergenceHistory
                            If Double.IsNaN(M) = False Then
                                Tnew = .Temperatura0 + (.Temperatura - .Temperatura0) / (1 - M)
                                Pnew = .Pressao0 + (.Pressao - .Pressao0) / (1 - M)
                                Wnew = .VazaoMassica0 + (.VazaoMassica - .VazaoMassica0) / (1 - M)
                            Else
                                Tnew = .Temperatura
                                Pnew = .Pressao
                                Wnew = .VazaoMassica
                            End If
                        End With

                End Select

            End If

            Dim copydata As Boolean = True

            ems.PropertyPackage.CurrentMaterialStream = ems

            If Me.CopyOnStreamDataError Then
                copydata = True
            Else
                If Not Tnew.IsValid Or Not Pnew.IsValid Or Not Wnew.IsValid Or Not ems.PropertyPackage.RET_VMOL(PropertyPackages.Phase.Mixture).Sum.IsValid Then copydata = False
            End If

            If Not Me.AccelerationMethod = AccelMethod.GlobalBroyden And copydata Then

                Dim tmp As Object = Nothing
                Me.PropertyPackage.CurrentMaterialStream = form.Collections.FlowsheetObjectCollection(Me.GraphicObject.InputConnectors(0).AttachedConnector.AttachedFrom.Name)

                Select Case Me.FlashType
                    Case Helpers.Recycle.FlashType.FlashTP
                        tmp = Me.PropertyPackage.DW_CalcEquilibrio_ISOL(PropertyPackages.FlashSpec.T, PropertyPackages.FlashSpec.P, Tnew, Pnew, 0)
                    Case Helpers.Recycle.FlashType.FlashPS
                        tmp = form.Options.SelectedPropertyPackage.DW_CalcEquilibrio_ISOL(PropertyPackages.FlashSpec.P, PropertyPackages.FlashSpec.S, Pnew, Snew, Tnew)
                    Case Helpers.Recycle.FlashType.FlashPH
                        tmp = form.Options.SelectedPropertyPackage.DW_CalcEquilibrio_ISOL(PropertyPackages.FlashSpec.P, PropertyPackages.FlashSpec.H, Pnew, Hnew, Tnew)
                    Case Helpers.Recycle.FlashType.None
                        'tmp = Me.PropertyPackage.DW_CalcEquilibrio_ISOL(PropertyPackages.FlashSpec.T, PropertyPackages.FlashSpec.P, Tnew, Pnew, 0)
                End Select

                Select Case Me.FlashType

                    Case Helpers.Recycle.FlashType.FlashPH, Helpers.Recycle.FlashType.FlashPS, Helpers.Recycle.FlashType.FlashTP

                        Dim xl, xv, T, P, H, S, wtotalx, wtotaly As Double
                        Dim Vx(ems.Phases(0).Compounds.Count - 1), Vy(ems.Phases(0).Compounds.Count - 1), Vwx(ems.Phases(0).Compounds.Count - 1), Vwy(ems.Phases(0).Compounds.Count - 1) As Double
                        xl = tmp(0)
                        xv = tmp(1)
                        T = tmp(2)
                        P = tmp(3)
                        H = tmp(4)
                        S = tmp(5)
                        Vx = tmp(8)
                        Vy = tmp(9)

                        Dim i As Integer = 0
                        Dim j As Integer = 0

                        Dim ms As DWSIM.SimulationObjects.Streams.MaterialStream
                        Dim cp As ConnectionPoint
                        cp = Me.GraphicObject.InputConnectors(0)
                        If cp.IsAttached Then
                            ms = form.Collections.FlowsheetObjectCollection(cp.AttachedConnector.AttachedFrom.Name)
                            Dim comp As DWSIM.Thermodynamics.BaseClasses.Compound
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
                            ms = form.Collections.FlowsheetObjectCollection(cp.AttachedConnector.AttachedTo.Name)
                            With ms
                                .PropertyPackage.CurrentMaterialStream = ms
                                .Phases(0).Properties.temperature = Tnew
                                .Phases(0).Properties.pressure = Pnew
                                .Phases(0).Properties.massflow = Wnew
                                .Phases(0).Properties.enthalpy = H
                                Dim comp As DWSIM.Thermodynamics.BaseClasses.Compound
                                j = 0
                                For Each comp In .Phases(0).Compounds.Values
                                    comp.MoleFraction = ems.Phases(0).Compounds(comp.Name).MoleFraction
                                    comp.MassFraction = ems.Phases(0).Compounds(comp.Name).MassFraction
                                    j += 1
                                Next
                                ms.PropertyPackage.DW_CalcVazaoMolar()
                                j = 0
                                For Each comp In .Phases(1).Compounds.Values
                                    comp.MoleFraction = Vx(j)
                                    comp.MassFraction = Vwx(j)
                                    j += 1
                                Next
                                j = 0
                                For Each comp In .Phases(2).Compounds.Values
                                    comp.MoleFraction = Vy(j)
                                    comp.MassFraction = Vwy(j)
                                    j += 1
                                Next
                                .Phases(0).Properties.massfraction = 1
                                .Phases(0).Properties.molarfraction = 1
                                .Phases(1).Properties.massfraction = wtotalx
                                .Phases(1).Properties.molarfraction = xl
                                .Phases(2).Properties.massfraction = wtotaly
                                .Phases(2).Properties.molarfraction = xv
                            End With
                        End If

                    Case Helpers.Recycle.FlashType.None

                        Dim msfrom, msto As DWSIM.SimulationObjects.Streams.MaterialStream
                        msfrom = form.Collections.FlowsheetObjectCollection(Me.GraphicObject.InputConnectors(0).AttachedConnector.AttachedFrom.Name)

                        If Not msfrom.Calculated And Not msfrom.AtEquilibrium Then
                            Throw New Exception(DWSIM.App.GetLocalString("RecycleStreamNotCalculated"))
                        End If

                        msto = form.Collections.FlowsheetObjectCollection(Me.GraphicObject.OutputConnectors(0).AttachedConnector.AttachedTo.Name)
                        msto.Assign(msfrom)
                        msto.AssignProps(msfrom)

                End Select

            End If

            If Me.IterationCount >= Me.MaximumIterations Then
                Me.IterationCount = 0
                Throw New TimeoutException(DWSIM.App.GetLocalString("RecycleMaxItsReached"))
            End If

            If Math.Abs(Me.ConvergenceHistory.TemperaturaE) > Me.ConvergenceParameters.Temperatura Or _
                Math.Abs(Me.ConvergenceHistory.PressaoE) > Me.ConvergenceParameters.Pressao Or _
                Math.Abs(Me.ConvergenceHistory.VazaoMassicaE) > Me.ConvergenceParameters.VazaoMassica Then

                Me.Converged = False

                'Call function to calculate flowsheet
                With objargs
                    .Calculated = True
                    .Name = Me.Name
                    .ObjectType = ObjectType.OT_Recycle
                End With

                form.CalculationQueue.Enqueue(objargs)

            Else

                If Me.IterationCount <> 0 Then Me.IterationsTaken = Me.IterationCount
                Me.IterationCount = 0

                Me.Converged = True

            End If

            Me.IterationCount += 1

        End Function

        Public Overrides Function DeCalculate() As Integer

            Dim form As Global.DWSIM.IFLowsheet = Me.Flowsheet

            Me.IterationCount = 0

        End Function

        Function MAX(ByVal Vv As Object)

            Dim n = UBound(Vv)
            Dim mx As Double

            If n >= 1 Then
                Dim i As Integer = 1
                mx = Vv(i - 1)
                i = 0
                Do
                    If Vv(i) > mx Then
                        mx = Vv(i)
                    End If
                    i += 1
                Loop Until i = n + 1
                Return mx
            Else
                Return Vv(0)
            End If

        End Function

        Public Overrides Function GetPropertyValue(ByVal prop As String, Optional ByVal su As Interfaces.IUnitsOfMeasure = Nothing) As Object

            If su Is Nothing Then su = New SystemsOfUnits.SI
            Dim cv As New SystemsOfUnits.Converter
            Dim value As Double = 0
            Dim propidx As Integer = Convert.ToInt32(prop.Split("_")(2))

            Select Case propidx

                Case 0
                    'PROP_RY_0	Maximum Iterations
                    value = Me.MaximumIterations
                Case 1
                    'PROP_RY_1	Mass Flow Tolerance
                    value = SystemsOfUnits.Converter.ConvertFromSI(su.massflow, Me.ConvergenceParameters.VazaoMassica)
                Case 2
                    'PROP_RY_2	Temperature Tolerance
                    value = SystemsOfUnits.Converter.ConvertFromSI(su.temperature, Me.ConvergenceParameters.Temperatura)
                Case 3
                    'PROP_RY_3	Pressure Tolerance
                    value = SystemsOfUnits.Converter.ConvertFromSI(su.pressure, Me.ConvergenceParameters.Pressao)
                Case 4
                    'PROP_RY_4	Mass Flow Error
                    value = SystemsOfUnits.Converter.ConvertFromSI(su.massflow, Me.ConvergenceHistory.VazaoMassicaE)
                Case 5
                    'PROP_RY_5	Temperature Error
                    value = SystemsOfUnits.Converter.ConvertFromSI(su.temperature, Me.ConvergenceHistory.TemperaturaE)
                Case 6
                    'PROP_RY_6	Pressure Error
                    value = SystemsOfUnits.Converter.ConvertFromSI(su.pressure, Me.ConvergenceHistory.PressaoE)
            End Select

            Return value



        End Function

        Public Overloads Overrides Function GetProperties(ByVal proptype As Interfaces.Enums.PropertyType) As String()
            Dim i As Integer = 0
            Dim proplist As New ArrayList
            Select Case proptype
                Case PropertyType.RO
                    For i = 4 To 6
                        proplist.Add("PROP_RY_" + CStr(i))
                    Next
                Case PropertyType.RW
                    For i = 0 To 3
                        proplist.Add("PROP_RY_" + CStr(i))
                    Next
                Case PropertyType.WR
                    For i = 0 To 3
                        proplist.Add("PROP_RY_" + CStr(i))
                    Next
                Case PropertyType.ALL
                    For i = 0 To 6
                        proplist.Add("PROP_RY_" + CStr(i))
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
                    'PROP_RY_0	Maximum Iterations
                    Me.MaximumIterations = propval
                Case 1
                    'PROP_RY_1	Mass Flow Tolerance
                    Me.ConvergenceParameters.VazaoMassica = SystemsOfUnits.Converter.ConvertToSI(su.massflow, propval)
                Case 2
                    'PROP_RY_2	Temperature Tolerance
                    Me.ConvergenceParameters.Temperatura = SystemsOfUnits.Converter.ConvertToSI(su.temperature, propval)
                Case 3
                    'PROP_RY_3	Pressure Tolerance
                    Me.ConvergenceParameters.Pressao = SystemsOfUnits.Converter.ConvertToSI(su.pressure, propval)

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
                    'PROP_RY_0	Maximum Iterations
                    value = ""
                Case 1
                    'PROP_RY_1	Mass Flow Tolerance
                    value = su.massflow
                Case 2
                    'PROP_RY_2	Temperature Tolerance
                    value = su.temperature
                Case 3
                    'PROP_RY_3	Pressure Tolerance
                    value = su.pressure
                Case 4
                    'PROP_RY_4	Mass Flow Error
                    value = su.massflow
                Case 5
                    'PROP_RY_5	Temperature Error
                    value = su.deltaT
                Case 6
                    'PROP_RY_6	Pressure Error
                    value = su.deltaP
            End Select

            Return value

        End Function
    End Class

End Namespace

Namespace DWSIM.SimulationObjects.SpecialOps.Helpers.Recycle

    Public Enum FlashType
        None
        FlashTP
        FlashPH
        FlashPS
    End Enum

    Public Enum AccelMethod
        None
        Wegstein
        Dominant_Eigenvalue
        GlobalBroyden
    End Enum

    <System.Serializable()> Public Class ConvergenceParameters

        Implements XMLSerializer.Interfaces.ICustomXMLSerialization

        Public Temperatura As Double = 0.1
        Public Pressao As Double = 0.1
        Public VazaoMassica As Double = 0.01
        Public FracaoVapor As Double = 0.01
        Public Entalpia As Double = 1
        Public Entropia As Double = 0.01
        Public Composicao As Double = 0.001

        Sub New()

        End Sub

        Public Function LoadData(data As System.Collections.Generic.List(Of System.Xml.Linq.XElement)) As Boolean Implements XMLSerializer.Interfaces.ICustomXMLSerialization.LoadData

            XMLSerializer.XMLSerializer.Deserialize(Me, data, True)

        End Function

        Public Function SaveData() As System.Collections.Generic.List(Of System.Xml.Linq.XElement) Implements XMLSerializer.Interfaces.ICustomXMLSerialization.SaveData

            Return XMLSerializer.XMLSerializer.Serialize(Me, True)

        End Function

    End Class

    <System.Serializable()> Public Class ConvergenceHistory

        Implements XMLSerializer.Interfaces.ICustomXMLSerialization

        Public Temperatura As Double = 0
        Public Pressao As Double = 0
        Public VazaoMassica As Double = 0
        Public Entalpia As Double = 0
        Public Entropia As Double = 0
        Public Temperatura0 As Double = 0
        Public Pressao0 As Double = 0
        Public VazaoMassica0 As Double = 0
        Public Entalpia0 As Double = 0
        Public Entropia0 As Double = 0

        Public TemperaturaE As Double = 0
        Public PressaoE As Double = 0
        Public VazaoMassicaE As Double = 0
        Public EntalpiaE As Double = 0
        Public EntropiaE As Double = 0
        Public TemperaturaE0 As Double = 0
        Public PressaoE0 As Double = 0
        Public VazaoMassicaE0 As Double = 0
        Public EntalpiaE0 As Double = 0
        Public EntropiaE0 As Double = 0

        Sub New()

        End Sub

        Public Function LoadData(data As System.Collections.Generic.List(Of System.Xml.Linq.XElement)) As Boolean Implements XMLSerializer.Interfaces.ICustomXMLSerialization.LoadData

            XMLSerializer.XMLSerializer.Deserialize(Me, data, True)

        End Function

        Public Function SaveData() As System.Collections.Generic.List(Of System.Xml.Linq.XElement) Implements XMLSerializer.Interfaces.ICustomXMLSerialization.SaveData

            Return XMLSerializer.XMLSerializer.Serialize(Me, True)

        End Function

    End Class

    <System.Serializable()> Public Class WegsteinParameters

        Public AccelFreq As Integer = 4
        Public Qmax As Double = 0
        Public Qmin As Double = -20
        Public AccelDelay = 2

    End Class

End Namespace
