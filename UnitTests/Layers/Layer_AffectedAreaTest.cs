using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using GeoAPI.Geometries;
using NetTopologySuite.Geometries;
using NUnit.Framework;
using SharpMap;
using SharpMap.Data;
using SharpMap.Data.Providers;
using SharpMap.Layers;
using SharpMap.Rendering.Decoration;
using LabelStyle = SharpMap.Styles.LabelStyle;

namespace UnitTests.Layers
{
    [TestFixture, Category("RequiresWindows")]
    public class Layer_AffectedAreaTest
    {
        public enum LabelLayerMode
        {
            BasicLabel,
            BasicLabelRot,
            TextOnPath,
            PathOnLabel,
            SineCurve,
            SineCurveClipped,
            SineCurveExtended
        }

        public enum SymbolizerMode
        {
            Rps,
            Cps,
            Pps,
            Lps
        }

        public enum PointAlignment
        {
            Horizontal,
            Vertical,
            Diagonal
        }

        private readonly float[] _rotations = new float[]
            {0f, 30f, 60f, 90f, 120f, 150f, 180f, 210f, 240f, 270f, 310f, 330f};

        /// <summary>
        /// Validate calculated affectedArea on 3 primary code paths of LabelLayer
        /// </summary>
        /// <param name="mode"></param>
        /// <param name="testRotations"></param>
        [NUnit.Framework.TestCase(LabelLayerMode.BasicLabel, true)]
        [NUnit.Framework.TestCase(LabelLayerMode.BasicLabelRot, true)]
        [NUnit.Framework.TestCase(LabelLayerMode.TextOnPath, true)]
        [NUnit.Framework.TestCase(LabelLayerMode.PathOnLabel, true)]
        public void LabelLayer_AffectedArea(LabelLayerMode mode, bool testRotations)
        {
            using (var map = new Map())
            {
                ConfigureMap(map);

                Envelope extents = null;
                switch (mode)
                {
                    case LabelLayerMode.BasicLabel:
                    case LabelLayerMode.BasicLabelRot:
                        AddBasicLabelLayers(map, mode);
                        extents = map.GetExtents();
                        extents.ExpandBy(0.125);
                        break;
                    case LabelLayerMode.TextOnPath:
                        AddTextOnPathLayers(map);
                        extents = map.GetExtents();
                        break;
                    case LabelLayerMode.PathOnLabel:
                        AddPathOnLabelLayers(map);
                        extents = map.GetExtents();
                        extents.ExpandBy(0.125);
                        break;
                }

                foreach (var rot in _rotations)
                {
                    SetMapTransform(map, rot);
                    map.ZoomToBox(extents, true);

                    var affectedArea = GetAffectedArea(map, (Layer) map.Layers[1]);
                    AddAffectedAreaLayer(map, affectedArea);

                    using (var img = map.GetMap())
                        img.Save(
                            Path.Combine(UnitTestsFixture.GetImageDirectory(this),
                                $"LabelLayer_{mode.ToString()}_{rot:000}.png"),
                            System.Drawing.Imaging.ImageFormat.Png);

                    // remove affected area layer
                    map.Layers.RemoveAt(2);
                    if (!testRotations) break;
                }
            }
        }

        private void ConfigureMap(Map map)
        {
            map.Size = new Size(800, 640);
            map.BackColor = Color.AliceBlue;
            map.SRID = 4326;
            map.Decorations.Add(new NorthArrow());
            map.Decorations.Add(new Disclaimer()
            {
                Text = "Affected Area envelope should surround label"
            });
        }

        private void SetMapTransform(Map map, float rotationDeg)
        {
            if (rotationDeg.Equals(0f))
            {
                map.MapTransform = null;
            }
            else
            {
                var matrix = new System.Drawing.Drawing2D.Matrix();
                matrix.RotateAt(rotationDeg, new PointF(map.Size.Width * 0.5f, map.Size.Height * 0.5f));
                map.MapTransform = matrix;
            }
        }

        private Envelope GetAffectedArea(Map map, Layer layer)
        {
            using (var img = new Bitmap(map.Size.Width, map.Size.Height))
            using (var g = Graphics.FromImage(img))
            {
                layer.Render(g, (MapViewport) map, out var affectedArea);
                return affectedArea;
            }
        }

        private void AddAffectedAreaLayer(Map map, Envelope affectedAreaEnv)
        {
            var coords = new Coordinate[]
            {
                new Coordinate(affectedAreaEnv.TopLeft()),
                new Coordinate(affectedAreaEnv.TopRight()),
                new Coordinate(affectedAreaEnv.BottomRight()),
                new Coordinate(affectedAreaEnv.BottomLeft()),
                new Coordinate(affectedAreaEnv.TopLeft())
            };
            
            var gp = new GeometryProvider(new Polygon(new LinearRing(coords)));
            var vLayer = new VectorLayer("Affected Area")
            {
                DataSource = gp,
                SRID = map.SRID
            };
            vLayer.Style.Fill = null;
            vLayer.Style.EnableOutline = true;

            map.Layers.Add(vLayer);
        }

        private void AddBasicLabelLayers(Map map, LabelLayerMode mode)
        {
            var fdt = new FeatureDataTable();
            fdt.Columns.Add(new DataColumn("ID", typeof(int)));
            fdt.Columns.Add(new DataColumn("LABEL", typeof(string)));

            var factory = GeoAPI.GeometryServiceProvider.Instance.CreateGeometryFactory(4236);
            var fdr = fdt.NewRow();
            fdr[0] = 1;
            fdr[1] = "Test Label";
            fdr.Geometry = factory.CreatePoint(new Coordinate(99, 13));
            fdt.AddRow(fdr);

            var vLyr = new VectorLayer("Basic Point", new GeometryFeatureProvider(fdt));
            map.Layers.Add(vLyr);

            var lLyr = new LabelLayer("Basic Point Labels")
            {
                DataSource = vLyr.DataSource,
                Enabled = true,
                LabelColumn = "LABEL",
                Style = new LabelStyle
                {
                    Offset = new PointF(20, -20),
                    HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Left,
                    VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Bottom
                }
            };

            if (mode == LabelLayerMode.BasicLabelRot)
                lLyr.Style.Rotation = 315f;

            map.Layers.Add(lLyr);
        }

        private void AddTextOnPathLayers(Map map)
        {
            string shapefile = System.IO.Path.GetFullPath(
                TestContext.CurrentContext.TestDirectory +
                @"\..\..\..\..\Examples\WinFormSamples\GeoData\World/shp_textonpath/DeMo_Quan5.shp");

            // TextOnPath (adopted from WinformSamples)
            var vLyr = new VectorLayer("TextOnPath");
            vLyr.DataSource = new ShapeFile(shapefile);
            (vLyr.DataSource as ShapeFile).Encoding = Encoding.UTF8;
            vLyr.Style.Fill = new SolidBrush(Color.Yellow);
            vLyr.Style.Line = new Pen(Color.Yellow, 4);
            vLyr.Style.Outline = new Pen(Color.Black, 5);
            ;
            vLyr.Style.EnableOutline = true;
            vLyr.SRID = map.SRID;
            (vLyr.DataSource as ShapeFile).FilterDelegate = TextOnPathFilter;
            map.Layers.Add(vLyr);

            var lLyr = new LabelLayer("TextOnPath labels");
            lLyr.DataSource = vLyr.DataSource;
            lLyr.Enabled = true;
            lLyr.LabelColumn = "tenduong";
            lLyr.LabelFilter = SharpMap.Rendering.LabelCollisionDetection.ThoroughCollisionDetection;
            lLyr.Style = new LabelStyle();
            lLyr.Style.ForeColor = Color.White;
            lLyr.Style.Font = new Font(FontFamily.GenericSerif, 9f, FontStyle.Bold);
            lLyr.Style.Halo = new Pen(Color.Black, 2f);
            lLyr.Style.IsTextOnPath = true;
            lLyr.Style.CollisionDetection = false;
            lLyr.Style.HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center;
            lLyr.Style.VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Top;
            lLyr.SRID = 4326;
            map.Layers.Add(lLyr);
        }

        public static bool TextOnPathFilter(FeatureDataRow fdr)
        {
            return fdr[1].ToString() == "Trần Phú";
        }

        private void AddPathOnLabelLayers(Map map)
        {
            var fdt = new FeatureDataTable();
            fdt.Columns.Add(new DataColumn("ID", typeof(int)));
            fdt.Columns.Add(new DataColumn("LABEL", typeof(string)));

            var factory = GeoAPI.GeometryServiceProvider.Instance.CreateGeometryFactory(4236);
            var fdr = fdt.NewRow();
            fdr[0] = 1;
            fdr[1] = "Test Label";
            fdr.Geometry = factory.CreateLineString(new[]
            {
                new Coordinate(98.5, 12.5),
                new Coordinate(99.5, 13.5)
            });
            fdt.AddRow(fdr);

            var vLyr = new VectorLayer("Basic Line", new GeometryFeatureProvider(fdt));
            vLyr.Style.Line = new Pen(Color.DodgerBlue, 2f);
            map.Layers.Add(vLyr);

            var lLyr = new LabelLayer("Basic Line Labels")
            {
                DataSource = vLyr.DataSource,
                Enabled = true,
                LabelColumn = "LABEL",
            };
            lLyr.Style.IsTextOnPath = false;
            map.Layers.Add(lLyr);
        }

        /// <summary>
        /// Validate PathLabel using NTS LengthIndexedLine (ie NOT using old TextOnPath)
        /// </summary>
        /// <param name="mode"></param>
        /// <param name="testRotations"></param>
        [NUnit.Framework.TestCase(LabelLayerMode.SineCurve, true, false, true)]
        [NUnit.Framework.TestCase(LabelLayerMode.SineCurveClipped, true, false, false)]
        [NUnit.Framework.TestCase(LabelLayerMode.SineCurveExtended, true, false, false)]
        public void PathLabel_AffectedArea(LabelLayerMode mode, bool testRotations, bool testHzAlign, bool testVtAlign)
        {
            var hzAlgins = new[]
            {
                LabelStyle.HorizontalAlignmentEnum.Center,
                LabelStyle.HorizontalAlignmentEnum.Left,
                LabelStyle.HorizontalAlignmentEnum.Right
            };

            var vtAlgins = new[]
            {
                LabelStyle.VerticalAlignmentEnum.Top, 
                LabelStyle.VerticalAlignmentEnum.Middle,
                LabelStyle.VerticalAlignmentEnum.Bottom
            };

            foreach (var hzAlign in hzAlgins)
            {
                foreach (var vtAlign in vtAlgins)
                {
                    using (var map = new Map())
                    {
                        ConfigureMap(map);

                        AddSineCurveLayers(map, mode, hzAlign, vtAlign);

                        if (mode == LabelLayerMode.SineCurveClipped)
                        {
                            map.ZoomToExtents();
                            map.Center = new Coordinate(map.Center.X - map.Envelope.Width * 0.25, map.Center.Y);
                            map.Zoom *= 0.65;
                        }

                        foreach (var rot in _rotations)
                        {
                            SetMapTransform(map, rot);

                            switch (mode)
                            {
                                case LabelLayerMode.SineCurve:
                                    map.ZoomToExtents();
                                    map.Zoom *= 1.25;
                                    break;
                                case LabelLayerMode.SineCurveClipped:
//                            map.ZoomToExtents();
//                            map.Zoom *= 0.6;
                                    break;
                                case LabelLayerMode.SineCurveExtended:
                                    map.ZoomToExtents();
                                    map.Zoom *= 1.25;
                                    break;
                            }

                            var affectedArea = GetAffectedArea(map, (Layer) map.Layers[1]);
                            if (affectedArea == null)
                                continue;
                            
                            AddAffectedAreaLayer(map, affectedArea);

                            using (var img = map.GetMap())
                                img.Save(
                                    Path.Combine(UnitTestsFixture.GetImageDirectory(this),
                                        $"PathLabel_{mode.ToString()}_Hz{hzAlign.ToString()}_Vt{vtAlign.ToString()}_{rot:000}.png"),
                                    System.Drawing.Imaging.ImageFormat.Png);

                            // remove affected area layer
                            map.Layers.RemoveAt(2);
                            if (!testRotations) break;
                        }
                    }
                    if (!testVtAlign) break;
                }
                if (!testHzAlign) break;
            }
        }

        private void AddSineCurveLayers(Map map, LabelLayerMode mode,  
            LabelStyle.HorizontalAlignmentEnum hzAlign, 
            LabelStyle.VerticalAlignmentEnum vtAlign)
        {
            string text;
            switch (mode)
            {
                case LabelLayerMode.SineCurveExtended:
                    text =
                        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam";//", quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.";
                    break;
                default:
                    text = "Lorem ipsum olor sit amet, consectetur adipisici elit";
                    break;
            }

            var fdt = new SharpMap.Data.FeatureDataTable();
            fdt.BeginInit();
            fdt.Columns.Add(new System.Data.DataColumn("ID", typeof(int)));
            fdt.Columns.Add(new System.Data.DataColumn("LABEL", typeof(string)));
            fdt.PrimaryKey = new[] {fdt.Columns[0]};
            fdt.EndInit();
            fdt.BeginLoadData();
            var fdr = (SharpMap.Data.FeatureDataRow) fdt.LoadDataRow(new object[] {1, text}, true);
            fdr.Geometry = CreateSineLine(new GeoAPI.Geometries.Coordinate(10, 10));
            fdt.EndLoadData();

            var vLyr = new SharpMap.Layers.VectorLayer("Geometry", new GeometryFeatureProvider(fdt));
            vLyr.Style.Line = new System.Drawing.Pen(System.Drawing.Color.Black, 4);
            vLyr.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            vLyr.SRID = map.SRID;
            map.Layers.Add(vLyr);

            var lLyr = new SharpMap.Layers.LabelLayer("Label") {DataSource = vLyr.DataSource, LabelColumn = "LABEL"};
            lLyr.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            lLyr.Style.ForeColor = System.Drawing.Color.Cyan;
            lLyr.Style.BackColor = new SolidBrush(Color.FromArgb(128,Color.LightSlateGray));
            //lLyr.Style.Halo = new Pen(Color.Yellow, 4);
            lLyr.Style.IgnoreLength = mode==LabelLayerMode.SineCurveExtended;
            lLyr.Style.HorizontalAlignment = hzAlign;
            lLyr.Style.VerticalAlignment = vtAlign;
            //ll.Style.IsTextOnPath = textOnPath;
            map.Layers.Add(lLyr);
        }

        private static GeoAPI.Geometries.ILineString CreateSineLine(GeoAPI.Geometries.Coordinate offset,
            double scaleY = 100)
        {
            var factory = new NetTopologySuite.Geometries.GeometryFactory();
            var cs = factory.CoordinateSequenceFactory.Create(181, 2);
            for (int i = 0; i <= 180; i++)
            {
                cs.SetOrdinate(i, GeoAPI.Geometries.Ordinate.X, offset.X + 2 * i);
                cs.SetOrdinate(i, GeoAPI.Geometries.Ordinate.Y, offset.Y + scaleY * System.Math.Sin(2d * i * System.Math.PI/180d));
            }
            return factory.CreateLineString(cs);        
        }

        [NUnit.Framework.TestCase(SymbolizerMode.Rps, PointAlignment.Horizontal, true)]
        [NUnit.Framework.TestCase(SymbolizerMode.Rps, PointAlignment.Vertical, false)]
        [NUnit.Framework.TestCase(SymbolizerMode.Rps, PointAlignment.Diagonal, false)]
        [NUnit.Framework.TestCase(SymbolizerMode.Cps, PointAlignment.Horizontal, true)]
        [NUnit.Framework.TestCase(SymbolizerMode.Cps, PointAlignment.Vertical, false)]
        [NUnit.Framework.TestCase(SymbolizerMode.Cps, PointAlignment.Diagonal, false)]
        [NUnit.Framework.TestCase(SymbolizerMode.Pps, PointAlignment.Horizontal, true)]
        [NUnit.Framework.TestCase(SymbolizerMode.Pps, PointAlignment.Vertical, false)]
        [NUnit.Framework.TestCase(SymbolizerMode.Pps, PointAlignment.Diagonal, false)]       
        [NUnit.Framework.TestCase(SymbolizerMode.Lps, PointAlignment.Horizontal, true)]
        [NUnit.Framework.TestCase(SymbolizerMode.Lps, PointAlignment.Vertical, false)]
        [NUnit.Framework.TestCase(SymbolizerMode.Lps, PointAlignment.Diagonal, false)]       
        public void PointSymbolizer_AffectedArea(SymbolizerMode symMode,  PointAlignment alignMode, bool testRotations)
        {
            using (var map = new Map())
            {
                ConfigureMap(map);

                switch (symMode)
                {
                    case SymbolizerMode.Rps:
                        AddRasterPointSymbolizerLayers(map, alignMode);
                        break;
                    case SymbolizerMode.Cps:
                        AddCharacterPointSymbolizerLayers(map, alignMode);
                        break;
                    case SymbolizerMode.Pps:
                        AddPathPointSymbolizerLayers(map, alignMode);
                        break;
                    case SymbolizerMode.Lps:
                        AddListPointSymbolizerLayers(map, alignMode);
                        break;
                }

                var extents = map.GetExtents();
                extents.ExpandBy(0.2);

                foreach (var rot in _rotations)
                {
                    SetMapTransform(map, rot);
                    map.ZoomToBox(extents, true);

                    var affectedArea = GetAffectedArea(map, (Layer) map.Layers[0]);
                    AddAffectedAreaLayer(map, affectedArea);

                    using (var img = map.GetMap())
                        img.Save(
                            Path.Combine(UnitTestsFixture.GetImageDirectory(this),
                                $"{symMode.ToString()}_{alignMode.ToString()}_{rot:000}.png"),
                            System.Drawing.Imaging.ImageFormat.Png);

                    // remove affected area layer
                    map.Layers.RemoveAt(2);
                    if (!testRotations) break;
                }
            }
        }

        private List<NetTopologySuite.Geometries.Point> GetSymbolizerPoints(PointAlignment mode)
        {
            var pts = new List<NetTopologySuite.Geometries.Point>();
            switch (mode)
            {
                case PointAlignment.Horizontal:
                    pts.Add(new NetTopologySuite.Geometries.Point(99,7));
                    pts.Add(new NetTopologySuite.Geometries.Point(100,7));
                    pts.Add(new NetTopologySuite.Geometries.Point(101,7));
                    break;
                case PointAlignment.Vertical:
                    pts.Add(new NetTopologySuite.Geometries.Point(99,7));
                    pts.Add(new NetTopologySuite.Geometries.Point(99,8));
                    pts.Add(new NetTopologySuite.Geometries.Point(99,9));
                    break;
                case PointAlignment.Diagonal:
                    pts.Add(new NetTopologySuite.Geometries.Point(99,7));
                    pts.Add(new NetTopologySuite.Geometries.Point(100,8));
                    pts.Add(new NetTopologySuite.Geometries.Point(101,9));
                    break;
            }
            return pts;
        }           
        
        private void AddRasterPointSymbolizerLayers(Map map, PointAlignment mode)
        {
            var pts = GetSymbolizerPoints(mode);
            
            var vLyr = new VectorLayer("RasterPoint", new GeometryFeatureProvider(pts.AsEnumerable()));
            var rps = new SharpMap.Rendering.Symbolizer.RasterPointSymbolizer();
            rps.Symbol = GetRasterSymbol();
            rps.Rotation = 30f;
            vLyr.Style.PointSymbolizer = rps;
            map.Layers.Add(vLyr);

            vLyr = new VectorLayer("ReferencePoint", new GeometryFeatureProvider(pts.AsEnumerable()));
            vLyr.Style.PointSize = 2f;
            map.Layers.Add(vLyr);
        }

        private System.Drawing.Image GetRasterSymbol()
        {
            var str = "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAABGdBTUEAALGPC/xhBQAAAAlwSFlzAAAOwQAADsEBuJFr7QAAABl0RVh0U29mdHdhcmUAcGFpbnQubmV0IDQuMC4yMfEgaZUAAAE8SURBVHhe7dpRDoIwEIThHpWbcHRkW2osJQouswTyTzLFWG3d79k0pTRd0bSqvXdF7fZpji1hzQOvsrzXfE7eKwC2hq8JR4gG+DZ8TShCJMCe4WvCEKIAjgxfE4IQAWCDjOM4vzwW+44cQQ3w7/A1cgQAhADe4WukCACIAM4avkaGEAkwDMOhfuYxAHtza4Ct4S0eAIsEAQAAytJtOLr8yC5eAAsAAOSzm/tcBQCAsnQbjgIAgD36AAAAAMur33kswJFu5dYAZwQAAPLZzX2uAgBAWboNRwEAwB6aAABAPru5z1UAAChLt+EoAADYQxMAAMhnN/e5CgAAZek2HAUAAHtoAgAA+ezmPlcBAKAs3YajAABgD00AACCf3dznKgAigK0/S3tzmz9LW89GkAxvfQMIaj/4zM6LoGl6AWGcInMnlc2ZAAAAAElFTkSuQmCC";
            byte[] imageBytes = Convert.FromBase64String(str);
            System.IO.MemoryStream ms = new System.IO.MemoryStream(imageBytes, 0, imageBytes.Length);
            ms.Write(imageBytes, 0, imageBytes.Length);
            System.Drawing.Image image = System.Drawing.Image.FromStream(ms, true);
            return image;
        }

        private void AddCharacterPointSymbolizerLayers(Map map, PointAlignment mode)
        {
            var pts = GetSymbolizerPoints(mode);
            
            var vLyr = new VectorLayer("CharPoint", new GeometryFeatureProvider(pts.AsEnumerable()));
            var cps  = new SharpMap.Rendering.Symbolizer.CharacterPointSymbolizer
            {
                Halo = 1,
                HaloBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Wheat),
                Rotation = 30f,
                Text = "xYz",
            };
            vLyr.Style.PointSymbolizer = cps;
            map.Layers.Add(vLyr);

            vLyr = new VectorLayer("ReferencePoint", new GeometryFeatureProvider(pts.AsEnumerable()));
            vLyr.Style.PointSize = 4f;
            map.Layers.Add(vLyr);
        }
        
        private void AddLinePointSymbolizerLayers(Map map, PointAlignment mode)
        {
            var pts = GetSymbolizerPoints(mode);
            
            var vLyr = new VectorLayer("LinePoint", new GeometryFeatureProvider(pts.AsEnumerable()));
            var cps  = new SharpMap.Rendering.Symbolizer.CharacterPointSymbolizer
            {
                Halo = 1,
                HaloBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Wheat),
                Text = "xYz",
            };
            vLyr.Style.PointSymbolizer = cps;
            map.Layers.Add(vLyr);

            vLyr = new VectorLayer("ReferencePoint", new GeometryFeatureProvider(pts.AsEnumerable()));
            vLyr.Style.PointSize = 4f;
            map.Layers.Add(vLyr);
        }

        private void AddPathPointSymbolizerLayers(Map map, PointAlignment mode)
        { 
            var pts = GetSymbolizerPoints(mode);
            
            var vLyr = new VectorLayer("PathPoint with 2 parts", new GeometryFeatureProvider(pts.AsEnumerable()));

            var gpTriangle1 = new System.Drawing.Drawing2D.GraphicsPath();
            gpTriangle1.AddPolygon(new [] { new System.Drawing.Point(0, 0), new System.Drawing.Point(5, 10), new System.Drawing.Point(10, 0), new System.Drawing.Point(0, 0), });
            var gpTriangle2 = new System.Drawing.Drawing2D.GraphicsPath();
            gpTriangle2.AddPolygon(new[] { new System.Drawing.Point(0, 0), new System.Drawing.Point(-5, -10), new System.Drawing.Point(-10, 0), new System.Drawing.Point(0, 0), });
            var pps = new
                SharpMap.Rendering.Symbolizer.PathPointSymbolizer(new[]
                                                        {
                                                            new SharpMap.Rendering.Symbolizer.PathPointSymbolizer.PathDefinition
                                                                {
                                                                    Path = gpTriangle1,
                                                                    Line =
                                                                        new System.Drawing.Pen(
                                                                        System.Drawing.Color.Red, 2),
                                                                    Fill =
                                                                        new System.Drawing.SolidBrush(
                                                                        System.Drawing.Color.DodgerBlue)
                                                                },
                                                            new SharpMap.Rendering.Symbolizer.PathPointSymbolizer.PathDefinition
                                                                {
                                                                    Path = gpTriangle2,
                                                                    Line =
                                                                        new System.Drawing.Pen(
                                                                        System.Drawing.Color.DodgerBlue, 2),
                                                                    Fill =
                                                                        new System.Drawing.SolidBrush(
                                                                        System.Drawing.Color.Red)
                                                                }

                                                        });

            vLyr.Style.PointSymbolizer = pps;
            map.Layers.Add(vLyr);

            vLyr = new VectorLayer("ReferencePoint", new GeometryFeatureProvider(pts.AsEnumerable()));
            vLyr.Style.PointSize = 4f;
            vLyr.Style.PointColor = Brushes.Yellow;
            map.Layers.Add(vLyr);
        }

        private void AddListPointSymbolizerLayers(Map map, PointAlignment mode)
        {
            var pts = GetSymbolizerPoints(mode);
            
            var vLyr = new VectorLayer("ListPoint with Pps and Cps", new GeometryFeatureProvider(pts.AsEnumerable()));
            var pps =
                SharpMap.Rendering.Symbolizer.PathPointSymbolizer.CreateSquare(new System.Drawing.Pen(System.Drawing.Color.Red, 2),
                    new System.Drawing.SolidBrush(
                        System.Drawing.Color.DodgerBlue), 20);

            var cps = new SharpMap.Rendering.Symbolizer.CharacterPointSymbolizer
            {
                Halo = 1,
                HaloBrush = new System.Drawing.SolidBrush(System.Drawing.Color.WhiteSmoke),
                Foreground = new System.Drawing.SolidBrush(System.Drawing.Color.Black),
                Font = new System.Drawing.Font("Arial", 12),
                CharacterIndex = 65
            };

            var lps = new SharpMap.Rendering.Symbolizer.ListPointSymbolizer { pps, cps };
            vLyr.Style.PointSymbolizer = lps;
            map.Layers.Add(vLyr);

            vLyr = new VectorLayer("ReferencePoint", new GeometryFeatureProvider(pts.AsEnumerable()));
            vLyr.Style.PointSize = 4f;
            vLyr.Style.PointColor = Brushes.Yellow;
            map.Layers.Add(vLyr);

        }
    }
}