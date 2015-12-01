package io.tintoy.actors.tests

import akka.actor.{ActorRef, ActorSystem, Props}
import akka.io.Tcp
import akka.testkit.{DefaultTimeout, ImplicitSender, TestKit, TestProbe}
import akka.util.ByteString

import com.typesafe.config.ConfigFactory

import java.nio.ByteOrder

import io.tintoy.actors.FramedTcpClient

import org.scalatest._

/**
 * Tests for [[FramedTcpClient]].
 */
class FramedTclClientSpec extends TestKit(
    ActorSystem("FramedTclClientSpec",
        ConfigFactory.defaultApplication()
      )
    ) with DefaultTimeout with ImplicitSender
    with WordSpecLike with Matchers
    with BeforeAndAfterAll with BeforeAndAfterEach {

  /**
   * The [[FramedTcpClient]] under test.
   */
  var framedTcpClient: ActorRef = null

  /**
   * Dummy target actor to send / receive unframed data.
   */
  var targetProbe: TestProbe = null

  /**
   * Dummy TCP client actor to send / receive framed data.
   */
  var tcpClientProbe: TestProbe = null

  "Framed TCP client configured for big-endian wire format" when {
    "sent a single packet of data that is exactly 1 frame long" must {
      "emit exactly 1 frame" in {
        val data = ByteString.fromString("Test")
        val frame: ByteString = BigEndian.withLengthPrefix(data)

        tcpClientProbe.send(framedTcpClient,
          Tcp.Received(frame)
        )
        targetProbe.expectMsg(
          FramedTcpClient.FrameReceived(data)
        )
      }
    }
  }

  /**
   * Big-endian data conversions.
   */
  object BigEndian {
    implicit val byteOrder = ByteOrder.BIG_ENDIAN

    /**
     * Prepend a length prefix to the specified data.
     * @param data The data.
     * @return The data with a little-endian length prefix.
     */
    def withLengthPrefix(data: ByteString): ByteString = {
      val prefix = ByteString.newBuilder.putInt(data.size).result()

      prefix ++ data
    }
  }

  /**
   * Little-endian data conversions.
   */
  object LittleEndian {
    implicit val byteOrder = ByteOrder.LITTLE_ENDIAN

    /**
     * Prepend a length prefix to the specified data.
     * @param data The data.
     * @return The data with a little-endian length prefix.
     */
    def withLengthPrefix(data: ByteString): ByteString = {
      val prefix = ByteString.newBuilder.putInt(data.size).result()

      prefix ++ data
    }
  }

  /**
   * Setup before test.
   */
  override protected def beforeEach(): Unit = {
    super.beforeEach()

    targetProbe = TestProbe()
    tcpClientProbe = TestProbe()

    framedTcpClient = system.actorOf(
      Props(
        classOf[FramedTcpClient],
        targetProbe.ref,
        tcpClientProbe.ref,
        FramedTcpClient.defaultMaxFrameSize,
        FramedTcpClient.BufferOverflowBehavior.Fail
      )
    )
  }

  /**
   * Tear-down after each test.
   */
  override protected def afterEach(): Unit = {
    system.stop(framedTcpClient)

    super.afterEach()
  }

  /**
   * Tear-down after all tests are complete.
   */
  override protected def afterAll(): Unit = {
    shutdown()

    super.afterAll()
  }
}
