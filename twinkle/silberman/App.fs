﻿namespace silberman

open SharpDX

open System
open System.Collections.Generic
open System.Collections.Concurrent
open System.Diagnostics
open System.Threading
open System.Threading.Tasks

open System.Windows.Forms

open Fundamental
open Device
open Visual
open Logical

module public App =


    type private ToUIMessage =
        | NewVisual of VisualTree
        | ShutDownUI  

    type private FromUIMessage =
        | Exception     of exn
        | Resized       of float32*float32
        | MouseChange   of MouseState
        | ShutDownApplication  

    type private SharedResources =
        {
            Key                     : int ref
            Brushes                 : ConcurrentDictionary<BrushKey                 , BrushDescriptor       * Direct2D1.Brush ref                           >
            TextFormats             : ConcurrentDictionary<TextFormatKey            , TextFormatDescriptor  * DirectWrite.TextFormat ref                    >
            Geometries              : ConcurrentDictionary<GeometryKey              , GeometryDescriptor    * Direct2D1.Geometry ref                        >
            TransformedGeometries   : ConcurrentDictionary<TransformedGeometryKey   , GeometryDescriptor    * Matrix3x2 * Direct2D1.TransformedGeometry ref >
        }
        static member New () = 
                {
                    Key                     = ref 0
                    Brushes                 = ConcurrentDictionary<BrushKey                 , BrushDescriptor       * Direct2D1.Brush ref                           > ()
                    TextFormats             = ConcurrentDictionary<TextFormatKey            , TextFormatDescriptor  * DirectWrite.TextFormat ref                    > ()
                    Geometries              = ConcurrentDictionary<GeometryKey              , GeometryDescriptor    * Direct2D1.Geometry ref                        > ()
                    TransformedGeometries   = ConcurrentDictionary<TransformedGeometryKey   , GeometryDescriptor    * Matrix3x2 * Direct2D1.TransformedGeometry ref > ()
                }
        member x.GenerateKey () = Interlocked.Increment x.Key
        member x.CreateBrush                 (bd : BrushDescriptor)         : BrushKey      = 
                let key = x.GenerateKey ()
                x.Brushes.[key] <- (bd, ref null)
                key
        member x.CreateTextFormat            (tfd : TextFormatDescriptor)   : TextFormatKey = 
                let key = x.GenerateKey ()
                x.TextFormats.[key] <- (tfd, ref null)
                key
        member x.CreateGeometry              (gd : GeometryDescriptor)      : GeometryKey   = 
                let key = x.GenerateKey ()
                x.Geometries.[key] <- (gd, ref null)
                key
        member x.CreateTransformedGeometry   (gd : GeometryDescriptor) (m : Matrix3x2) : GeometryKey =
                let key = x.GenerateKey ()
                x.TransformedGeometries.[key] <- (gd, m, ref null)
                key

    let private ShowForm 
        (title      : string                                    ) 
        (width      : float32                                   )
        (height     : float32                                   ) 
        (ct         : CancellationToken                         )
        (toui       : ConcurrentQueue<ToUIMessage>              )
        (fromui     : BlockingQueue<FromUIMessage>              )
        (shared     : SharedResources                           )
        =

        use onExitShutDown      = OnExit <| fun () -> ignore (fromui.Enqueue ShutDownApplication)

        use form                = new Windows.RenderForm(title)

        form.ClientSize         <- System.Drawing.Size(int width,int height)

        let device              = ref <| new WindowedDevice(form)

        let disposeDevice ()    = TryDispose !device
        let recreateDevice ()   = disposeDevice ()
                                  device := new WindowedDevice(form)
                                  let d = !device
                                  ignore <| fromui.Enqueue (Resized (d.Width, d.Height))
                                  

        use onExitDisposeDevice = OnExit disposeDevice

        let mouseState          = ref <| MouseState.Zero

        let hasButton (btn : MouseButtons) (mb : MouseButtons) = (btn &&& mb) = mb
        let toMouseButtonStates (btn : MouseButtons) (mb : MouseButtons) (mbs : MouseButtonStates) = 
            if (btn &&& mb) = mb then mbs
            else MouseButtonStates.Empty

        let getButtonState (e : MouseEventArgs) =
            let btn = e.Button
            
            let mbs = 
                    toMouseButtonStates btn MouseButtons.Left   MouseButtonStates.Left      |||
                    toMouseButtonStates btn MouseButtons.Middle MouseButtonStates.Middle    |||
                    toMouseButtonStates btn MouseButtons.Right  MouseButtonStates.Right

            mbs

        let getCoordinate (e : MouseEventArgs) = 
                Vector2(float32 e.X, float32 e.Y)

        let resize              = EventHandler(fun o e -> recreateDevice ())
        let mouseUp             = MouseEventHandler(fun o e -> 
                                    let btn = e.Button 
                                    if btn = MouseButtons.None then ()
                                    else
                                        let ms = !mouseState
                                        let bs = getButtonState e
                                        let state = ms.ButtonState.Difference  bs
                                        mouseState := MouseState.New state ms.Coordinate
                                    fromui.Enqueue (MouseChange !mouseState)
                                    )
        let mouseDown           = MouseEventHandler(fun o e -> 
                                    let btn = e.Button 
                                    if btn = MouseButtons.None then ()
                                    else
                                        let ms = !mouseState
                                        let bs = getButtonState e
                                        let state = ms.ButtonState.Union bs
                                        mouseState := MouseState.New state ms.Coordinate
                                    fromui.Enqueue (MouseChange !mouseState)
                                    )
        let mouseMove           = MouseEventHandler(fun o e ->  
                                    let ms = !mouseState
                                    mouseState := MouseState.New ms.ButtonState <| getCoordinate e
                                    )

        form.Resize.AddHandler      resize
        form.MouseUp.AddHandler     mouseUp
        form.MouseDown.AddHandler   mouseDown
        form.MouseMove.AddHandler   mouseMove

        use onExitRemoveHandler = OnExit <| fun () -> 
                                    form.MouseClick.RemoveHandler mouseMove
                                    form.MouseClick.RemoveHandler mouseDown
                                    form.MouseClick.RemoveHandler mouseUp
                                    form.Resize.RemoveHandler resize

        let shutdown () = ignore <| Win32.Interop.Post form.Handle 130u System.IntPtr.Zero System.IntPtr.Zero

        let vt = ref VisualTree.NoVisual

        try 
            Windows.RenderLoop.Run(form, fun () -> 
                let d = !device

                d.Draw <| fun d2dRenderTarget -> 
                
                    d2dRenderTarget.Clear(AsNullable <| Color.White.ToColor4())

                    let appState = ApplicationState.New (CurrentTime()) <| !mouseState 

                    Visual.RenderTree appState d2dRenderTarget d !vt

                if ct.IsCancellationRequested then shutdown ()

                let msg = RefOf<ToUIMessage>
                while toui.TryDequeue msg do
                    match !msg with
                    | ShutDownUI    -> shutdown ()
                    | NewVisual nvt -> vt := nvt

                )

            
        with
            | e -> ignore <| fromui.Enqueue (Exception e)

    let Show 
        (title  : string) 
        (width  : int   )
        (height : int   )
        (body   : Logical.Foundation.Element) = 
        
        let formProcessor ct toui fromui sharedResources = async {
                do! Async.SwitchToThread2 ApartmentState.STA ThreadPriority.AboveNormal

                ShowForm title (float32 width) (float32 height) ct toui fromui sharedResources

                return ()
            }

        async {
            let fromui  = BlockingQueue<FromUIMessage>() 
            let toui    = ConcurrentQueue<ToUIMessage>() 

            use directWrite         = new DirectWrite()

            let! ct = Async.CancellationToken

            let sharedResources = SharedResources.New ()

            Async.StartImmediate <| formProcessor ct toui fromui sharedResources

            let nextRebuild = ref <| CurrentTime () + 0.1F

            let cont = ref true

            let elementContext  = Logical.Foundation.ElementContext.New 
                                    sharedResources.CreateBrush
                                    sharedResources.CreateTextFormat
                                    sharedResources.CreateGeometry
                                    sharedResources.CreateTransformedGeometry
                                    directWrite.EstimateTextSize

            let document = Logical.Standard.DocumentElement (elementContext)

            document.Set Properties.Child <| Some body

            let mouseState      = ref <| MouseState.Zero
            let nextMouseState  = ref <| MouseState.Zero

            let available = ref <| Available.New (AvailableUnit.Bound <| float32 width) (AvailableUnit.Bound <| float32 height)
            let placement = ref <| Placement.New 0.F 0.F (float32 width) (float32 height)

            try 

                while !cont && not <| ct.IsCancellationRequested do
                
                    let waitFor = int <| 100.F *  max 0.F (!nextRebuild - CurrentTime ()) + 0.5F

                    let! fromMessages = fromui.AsyncDequeue waitFor

                    if fromMessages.Length > 0 then
                        for fromMessage in fromMessages do
                            match fromMessage with
                            | ShutDownApplication   -> cont := false
                            | Resized (w,h)         -> document.InvalidateMeasurement ()
                            | MouseChange ms        -> nextMouseState := ms 
                            | Exception e           -> ()
                    else
                        nextRebuild := CurrentTime () + 1000.F

                        ignore <| document.MeasureElement !available
                        ignore <| document.PlaceElement !placement
                        let vt = document.Render ()

                        toui.Enqueue <| NewVisual vt
                        ()

            finally 
                toui.Enqueue <| ShutDownUI

            return ()            
        }
        


