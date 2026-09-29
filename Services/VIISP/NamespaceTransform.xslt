<xsl:stylesheet version="1.0"
 xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
 xmlns:ns2="http://www.epaslaugos.lt/services/authentication">
 <xsl:output omit-xml-declaration="yes" indent="yes"/>

 <xsl:template match="node()|@*">
  <xsl:copy>
   <xsl:apply-templates select="node()|@*"/>
  </xsl:copy>
 </xsl:template>

 <xsl:template match="*">
  <xsl:element name="ns2:{name()}" namespace="http://www.epaslaugos.lt/services/authentication">
    <xsl:apply-templates select="node()|@*"/>
  </xsl:element>
 </xsl:template>

</xsl:stylesheet>