﻿namespace twinkle

open silberman
open silberman.Visual

open System
open System.Collections.Generic

open Elements
open Elements.Events
open Elements.Properties

open SharpDX

open Foundation

module TwinkleGame =

    let Facets = 4

    [<Measure>]
    type Columns

    [<Measure>]
    type Rows

    [<Measure>]
    type X

    [<Measure>]
    type Y

    type TwinkleColor =
        | Red
        | Orange
        | Yellow
        | Green
        | Blue
        | Indigo
        | Violet

    type Direction =
        | Left
        | Up
        | Right
        | Down

    let DirectionToInt (d : Direction) =
        match d with
        | Left  -> 0
        | Up    -> 1
        | Right -> 2
        | Down  -> 3

    let IntToDirection (i : int) =
        match ((i % Facets) + Facets) % Facets with
        | 0 -> Left
        | 1 -> Up
        | 2 -> Right
        | 3 -> Down
        | _ -> Left

    type VisualCell =
        {
            mutable Rotation            : AnimatedFloat
            mutable LeftButtonPressed   : bool
        }
        static member New () =
            {
                Rotation            = 0.F |> Animated.Constant
                LeftButtonPressed   = false
            }

    type Cell =
        {
            Colors                  : TwinkleColor[]
            mutable Direction       : Direction
            Visual                  : VisualCell
        }

        member x.GetRotatedColor (d : Direction) : TwinkleColor =
            let dir = ((DirectionToInt d) + (DirectionToInt x.Direction)) % Facets
            x.Colors.[dir]

        member x.GetColor (d : Direction) : TwinkleColor =
            let dir = DirectionToInt d
            x.Colors.[dir]

        member x.SetColor (d : Direction) (c : TwinkleColor) =
            let dir = DirectionToInt d
            x.Colors.[dir] <- c

        member x.Rotate (i : int) =
            let dir = (DirectionToInt x.Direction) + i
            x.Direction <- IntToDirection dir

        member x.RotationInDegree = float32 <| 2.0 * Math.PI * (float <| DirectionToInt x.Direction) / (float x.Colors.Length)

        static member New () =
                        {
                            Colors      = [| for i in 0..3 -> Red |]
                            Direction   = Left
                            Visual      = VisualCell.New ()
                        }

    type Board =
        {
            Cells   : Cell[][]
            Columns : int<Columns>
            Rows    : int<Rows>
        }
        static member New c r =
                        {
                            Cells   = [| for i in 0..(int <| r - 1<Rows>) -> [| for i in 0..(int <| c - 1<Columns>) -> Cell.New () |] |]
                            Columns = c
                            Rows    = r
                        }

    let VisitCells
        (v          : Cell->int<X>->int<Y>->unit    )
        (b          : Board                         )
        =
        let lasty = int <| b.Rows - 1<Rows>
        let lastx = int <| b.Columns - 1<Columns>
        for y in 0..lasty do
            let dy = y * 1<Y>
            for x in 0..lastx do
                let dx = x * 1<X>
                let cell = b.Cells.[y].[x]
                v cell dx dy

    let VisitFacets
        (v          : Cell->int<X>->int<Y>->Direction->unit )
        (b          : Board                                 )
        =
        let lasty = int <| b.Rows - 1<Rows>
        let lastx = int <| b.Columns - 1<Columns>
        for y in 0..lasty do
            let dy = y * 1<Y>
            for x in 0..lastx do
                let dx = x * 1<X>
                let cell = b.Cells.[y].[x]
                v cell dx dy Left
                v cell dx dy Up
                v cell dx dy Right
                v cell dx dy Down

    let VisitAdjacent
        (isolated   : Cell->int<X>->int<Y>->Direction->unit                                 )
        (adjacent   : Cell->int<X>->int<Y>->Direction->Cell->int<X>->int<Y>->Direction->unit)
        (b          : Board                                                                 )
        =
        let lasty = int <| b.Rows - 1<Rows>
        let lastx = int <| b.Columns - 1<Columns>
        for y in 0..lasty do
            let dy = y * 1<Y>
            for x in 0..lastx do
                let dx = x * 1<X>
                let cell = b.Cells.[y].[x]

                if x = 0 then
                    isolated cell dx dy Left

                if x < lastx then
                    let ocell = b.Cells.[y].[x + 1]
                    adjacent cell dx dy Right ocell (dx + 1<X>) dy Left
                else
                    isolated cell dx dy Right

                if y = 0 then
                    isolated cell dx dy Up

                if y < lasty then
                    let ocell = b.Cells.[y + 1].[x]
                    adjacent cell dx dy Down ocell dx (dy + 1<Y>) Up
                else
                    isolated cell dx dy Down


    let CreateBoard (random : Random) c r =
        let color () =
            let i = random.Next (0, 7)
            match i with
            | 0 -> Red
            | 1 -> Orange
            | 2 -> Yellow
            | 3 -> Green
            | 4 -> Blue
            | 5 -> Indigo
            | 6 -> Violet
            | _ -> Red
        let b = Board.New c r
        b |> VisitAdjacent
                (fun c _ _ d -> c.SetColor d <| color ())
                (fun c _ _ d oc _ _ od ->
                    let cl = color ()
                    c.SetColor d cl
                    oc.SetColor od cl
                )
        b

    let ShakeBoard (random : Random) (b : Board) =
        b |> VisitCells (fun c _ _ -> c.Rotate <| random.Next(0, Facets))
        b

    let UpdateVisual (b : Board) =
        b |> VisitCells
                (fun c _ _ ->
                    let r = c.RotationInDegree
                    c.Visual.Rotation <- r |> Animated.Constant
                )
        b

    let CheckWinCondition (b : Board) =
        let mismatches = ref 0
        b |> VisitAdjacent
                (fun c _ _ d -> ())
                (fun c _ _ d oc _ _ od ->
                    let cl  = c.GetRotatedColor d
                    let ocl = oc.GetRotatedColor od
                    if cl <> ocl then
                        mismatches := !mismatches + 1
                )
        !mismatches = 0

    type FromVisualMessage =
            | CellClicked of Cell

    module Elements =

        type CreateGameContext = 
            {
                Context         : ElementContext
                StrokeKey       : BrushKey
                OverlayKey      : BrushKey
                FillKeys        : Map<TwinkleColor, BrushKey>
                UnitSquareKey   : GeometryKey
                TriangleKey     : GeometryKey
            }
            static member New c s o f u t =
                {
                    Context         = c
                    StrokeKey       = s
                    OverlayKey      = o
                    FillKeys        = f
                    UnitSquareKey   = u
                    TriangleKey     = t
                }

        type GameElement() as x =
            inherit Element()

            let side        = 100.F
            let halfside    = side / 2.0F

            let fromVisual = BlockingQueue<FromVisualMessage> ()

            let stroke      = SolidBrush Color.Black

            let overlay     = SolidBrush Color.Black

            let fills       =
                [
                    Red     , SolidBrush Color.Red
                    Orange  , SolidBrush Color.Orange
                    Yellow  , SolidBrush Color.Yellow
                    Green   , SolidBrush Color.Green
                    Blue    , SolidBrush Color.Blue
                    Indigo  , SolidBrush Color.Indigo
                    Violet  , SolidBrush Color.Violet
                ]

            let createVisualFacet
                (context    : CreateGameContext             )
                (c          : Cell                          )
                (x          : int<X>                        )
                (y          : int<Y>                        )
                (direction  : Direction                     ) =
                let v = c.Visual
                let s = context.StrokeKey                       |> Animated.Brush.Opaque
                let f = context.FillKeys.[c.GetColor direction] |> Animated.Brush.Opaque
                let r = float32 Math.PI * 2.0F / 4.0F + float32 Math.PI * 2.0F * (float32 <| DirectionToInt direction) / (float32 Facets)
                let ft =
                    Matrix3x2.Rotation r                *
                    Matrix3x2.Scaling side

                let sw= 2.0F |> Animated.Constant
                let key = context.Context.CreateTransformedGeometry context.TriangleKey ft
                VisualTree.TransformedGeometry (key, s, f, sw)

            let createVisualOverlay
                (context    : CreateGameContext             )
                (c          : Cell                          ) =
                let rect = RectangleF(-halfside, -halfside, side, side)
                let v = c.Visual
                let s = Animated.Brush.Transparent
                let f (s : ApplicationState) =
                    if rect.Contains s.CurrentMouse.Coordinate then
                        let leftButtonPressed = s.CurrentMouse.ButtonState.HasFlag MouseButtonStates.Left
                        if v.LeftButtonPressed && not leftButtonPressed then
                            fromVisual.Enqueue <| CellClicked c
                        v.LeftButtonPressed <- leftButtonPressed
                        context.OverlayKey, 0.5F
                    else
                        v.LeftButtonPressed <- false
                        InvalidId, 0.F

                let ft =
                    Matrix3x2.Scaling side

                let sw= 2.0F |> Animated.Constant
                let key = context.Context.CreateTransformedGeometry context.UnitSquareKey ft
                VisualTree.TransformedGeometry (key, s, f, sw)

            let random  = Random ()

            let board   =   CreateBoard random 6<Columns> 6<Rows>
                            |> ShakeBoard random
                            |> UpdateVisual

            let createVisual
                (context    : CreateGameContext             )
                =
                let outerGroup = List<VisualTree>()
                board |> VisitCells (fun c x y ->
                                        let innerGroup = List<VisualTree>()

                                        let v = c.Visual

                                        let t (s : ApplicationState) =
                                            let r = v.Rotation s
                                            Matrix3x2.Rotation r                                        *
                                            Matrix3x2.Translation (halfside + side * float32 x, halfside + side * float32 y)
                                        let rt (s : ApplicationState) =
                                            let r = v.Rotation s
                                            Matrix3x2.Translation (- halfside - side * float32 x, -halfside - side * float32 y)  *
                                            Matrix3x2.Rotation -r

                                        let facet = createVisualFacet context c x y

                                        innerGroup.Add <| facet Left
                                        innerGroup.Add <| facet Up
                                        innerGroup.Add <| facet Right
                                        innerGroup.Add <| facet Down
                                        innerGroup.Add <| createVisualOverlay context c

                                        let g = VisualTree.Group <| innerGroup.ToArray ()
                                        let t = VisualTree.Transform (t,rt, g)
                                        outerGroup.Add t
                                    )
                VisualTree.Group <| outerGroup.ToArray ()

            let gameLoop =
                async {
                    do! Async.SwitchToNewThread ()
                    while true do
                        let! messages = fromVisual.AsyncDequeue 1000

                        for message in messages do
                            match message with
                            | CellClicked cell  ->
                                    let ct = CurrentTime ()
                                    let nt = ct + 0.2F
                                    let pd = cell.RotationInDegree
                                    cell.Rotate 1
                                    let d   = cell.RotationInDegree
                                    let nd  = d + (float32 <| 2.0 * Math.PI)
                                    let cd  = if (abs <| d - pd) < (abs <| nd - pd) then d else nd
                                    let a = Animated.Float Animated.Ease.Linear ct nt pd cd
                                    cell.Visual.Rotation <- a

                    return ()
                }

            let mutable visualTree = None

            do
                x.SetEventHandler Events.Attached <| fun eh _ -> Async.StartImmediate gameLoop
                                                                 true
                x.SetEventHandler Events.Detached <| fun eh _ -> visualTree <- None
                                                                 true

            override x.OnMeasureContent a =
                        let width   = (float32 board.Columns) * side
                        let height  = (float32 board.Rows) * side
                        Measurement.FromSize2 <| Size2F (width, height)

            override x.OnRenderContent  (o : Placement)
                                        (i : Placement) =
                        let transform (state : ApplicationState)    =
                            Matrix3x2.Translation (i.X, i.Y)
                        let rtransform (state : ApplicationState)   =
                            Matrix3x2.Translation (-i.X, -i.Y)

                        let vt =    match visualTree, x.Context with
                                    | Some vt,_         ->  vt
                                    | _, Some context   ->
                                        let strokeKey       =   context.CreateBrush stroke
                                        let overlayKey      =   context.CreateBrush overlay
                                        let fillKeys        =   fills
                                                                |> List.map (fun (k,v) -> k,context.CreateBrush v)
                                                                |> Map.ofList
                                        let unitSquareKey   =   context.CreateGeometry <|
                                                                    PolygonGeometry 
                                                                        [|
                                                                            +0.5F, +0.5F
                                                                            +0.5F, -0.5F
                                                                            -0.5F, -0.5F
                                                                            -0.5F, +0.5F
                                                                        |]                                     
                                        let triangleKey     =   context.CreateGeometry <|
                                                                    PolygonGeometry 
                                                                        [|
                                                                             +0.0F, +0.0F
                                                                             +0.5F, +0.5F
                                                                             -0.5F, +0.5F
                                                                        |]
                                        let context         = CreateGameContext.New
                                                                context
                                                                strokeKey
                                                                overlayKey
                                                                fillKeys
                                                                unitSquareKey
                                                                triangleKey
                                                                        
                                        let vt          = createVisual context 
                                        visualTree <- Some <| vt
                                        vt
                                    | _ -> VisualTree.NoVisual
                        VisualTree.Transform (transform, rtransform, vt)

    let Game (pvs : PropertyValue list) = Elements.CreateElement<Elements.GameElement> pvs


